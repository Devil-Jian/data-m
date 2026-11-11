using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed partial class ExperimentWorkspace
{
    public sealed record UndoAssetMove(
        string Id,string OldResult,string OldGroup,string NewResult,string NewGroup,string RelativePath);
    public sealed record UndoGroupOwner(string Id,string OriginalResult);
    public sealed record UndoAssetBatch(UndoAssetMove[] Assets,UndoGroupOwner[] ChangedOwners);
    public sealed record UndoResultMove(string ResultId,string OldProject,string NewProject);

    private void InitializeUndo()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS workspace_undo(
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL CHECK(kind IN ('assets','result')),
                payload TEXT NOT NULL,
                undone INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void RecordUndo(SqliteConnection db,SqliteTransaction tx,string kind,string payload)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO workspace_undo(kind,payload,created_at) VALUES($kind,$payload,$now)";
        cmd.Parameters.AddWithValue("$kind",kind);
        cmd.Parameters.AddWithValue("$payload",payload);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
    }

    private static string GetGroupOwner(SqliteConnection db,SqliteTransaction tx,string groupId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT result_id FROM experiment_groups WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",groupId);
        return cmd.ExecuteScalar() as string ?? throw new InvalidDataException("数据组不存在。");
    }

    public bool CanUndoMove
    {
        get
        {
            using var db=Connect();
            using var cmd=db.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM workspace_undo WHERE undone=0";
            return Convert.ToInt64(cmd.ExecuteScalar())>0;
        }
    }

    /// <summary>
    /// Undo the latest successful logical move, even after an app restart.
    /// Atomic and conflict-checked; a failure leaves the journal and assets intact.
    /// Does not alter the original disk files or SHA-256 content blobs.
    /// </summary>
    public bool UndoLastMove()
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        long seq;
        string kind,payload;
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="SELECT seq,kind,payload FROM workspace_undo WHERE undone=0 ORDER BY seq DESC LIMIT 1";
            using var reader=cmd.ExecuteReader();
            if(!reader.Read())return false;
            seq=reader.GetInt64(0);
            kind=reader.GetString(1);
            payload=reader.GetString(2);
        }
        if(kind=="result")
        {
            var item=JsonSerializer.Deserialize<UndoResultMove>(payload)
                ?? throw new InvalidDataException("结果单元撤销记录无效。");
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="""
                UPDATE experiment_results SET project_id=$old
                WHERE id=$id AND project_id=$new AND is_deleted=0
                """;
            cmd.Parameters.AddWithValue("$id",item.ResultId);
            cmd.Parameters.AddWithValue("$old",item.OldProject);
            cmd.Parameters.AddWithValue("$new",item.NewProject);
            if(cmd.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("结果单元已再次变化，无法安全撤销。");
            RefreshSampleIndex(db,tx,item.OldProject);
            RefreshSampleIndex(db,tx,item.NewProject);
        }
        else if(kind=="assets")
        {
            var batch=JsonSerializer.Deserialize<UndoAssetBatch>(payload)
                ?? throw new InvalidDataException("文件移动撤销记录无效。");
            var ids=batch.Assets.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
            // Check all assets first to avoid undoing a move that was subsequently modified.
            foreach(var item in batch.Assets)
            {
                using var check=db.CreateCommand();
                check.Transaction=tx;
                check.CommandText="""
                    SELECT result_id,group_id,relative_path FROM experiment_assets
                    WHERE id=$id AND is_deleted=0
                    """;
                check.Parameters.AddWithValue("$id",item.Id);
                using var rd=check.ExecuteReader();
                if(!rd.Read()||rd.GetString(0)!=item.NewResult||
                   rd.GetString(1)!=item.NewGroup||rd.GetString(2)!=item.RelativePath)
                    throw new InvalidOperationException("文件在移动后再次变更，请先处理较新的操作。");
            }
            foreach(var item in batch.Assets)
            {
                using var conflict=db.CreateCommand();
                conflict.Transaction=tx;
                conflict.CommandText="""
                    SELECT COUNT(*) FROM experiment_assets
                    WHERE result_id=$old AND relative_path=$path AND id<>$id
                    """;
                conflict.Parameters.AddWithValue("$old",item.OldResult);
                conflict.Parameters.AddWithValue("$path",item.RelativePath);
                conflict.Parameters.AddWithValue("$id",item.Id);
                if(Convert.ToInt64(conflict.ExecuteScalar())>0)
                    throw new IOException("原位置出现同名文件，无法安全撤销："+item.RelativePath);
            }
            foreach(var group in batch.ChangedOwners)
            {
                using var others=db.CreateCommand();
                others.Transaction=tx;
                others.CommandText="SELECT id FROM experiment_assets WHERE group_id=$group";
                others.Parameters.AddWithValue("$group",group.Id);
                using var rd=others.ExecuteReader();
                while(rd.Read())
                    if(!ids.Contains(rd.GetString(0)))
                        throw new InvalidOperationException("移动后的数据组已有新文件，无法安全撤销。");
            }
            foreach(var group in batch.ChangedOwners)
            {
                using var owner=db.CreateCommand();
                owner.Transaction=tx;
                owner.CommandText="UPDATE experiment_groups SET result_id=$result WHERE id=$id";
                owner.Parameters.AddWithValue("$result",group.OriginalResult);
                owner.Parameters.AddWithValue("$id",group.Id);
                owner.ExecuteNonQuery();
            }
            foreach(var item in batch.Assets)
            {
                using var cmd=db.CreateCommand();
                cmd.Transaction=tx;
                cmd.CommandText="""
                    UPDATE experiment_assets SET result_id=$result,group_id=$group
                    WHERE id=$id
                    """;
                cmd.Parameters.AddWithValue("$result",item.OldResult);
                cmd.Parameters.AddWithValue("$group",item.OldGroup);
                cmd.Parameters.AddWithValue("$id",item.Id);
                cmd.ExecuteNonQuery();
            }
            RefreshSampleIndexForResults(db,tx,
                batch.Assets.SelectMany(x=>new[]{x.OldResult,x.NewResult}));
        }
        else throw new InvalidDataException("未知撤销类型。");
        using(var done=db.CreateCommand())
        {
            done.Transaction=tx;
            done.CommandText="UPDATE workspace_undo SET undone=1 WHERE seq=$seq";
            done.Parameters.AddWithValue("$seq",seq);
            done.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }
}
