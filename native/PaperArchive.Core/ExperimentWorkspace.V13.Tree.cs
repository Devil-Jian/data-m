using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed partial class ExperimentWorkspace
{
    private void InitializeV13Tree()
    {
        using var db=Connect();
        AddColumn(db,"experiment_projects","is_deleted","INTEGER NOT NULL DEFAULT 0");
        AddColumn(db,"experiment_results","is_deleted","INTEGER NOT NULL DEFAULT 0");
    }

    public void MoveResult(string resultId,string targetProjectId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        string original;
        using(var query=db.CreateCommand())
        {
            query.Transaction=tx;
            query.CommandText="SELECT project_id FROM experiment_results WHERE id=$id AND is_deleted=0";
            query.Parameters.AddWithValue("$id",resultId);
            original=query.ExecuteScalar() as string ??
                throw new KeyNotFoundException("源结果单元不存在。");
        }
        if(original==targetProjectId)return;
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            UPDATE experiment_results SET project_id=$target WHERE id=$result AND is_deleted=0
            AND EXISTS(SELECT 1 FROM experiment_projects WHERE id=$target AND is_deleted=0)
            """;
        cmd.Parameters.AddWithValue("$target",targetProjectId);
        cmd.Parameters.AddWithValue("$result",resultId);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("目标实验项目或源结果单元不存在。");
        RecordUndo(db,tx,"result",System.Text.Json.JsonSerializer.Serialize(
            new UndoResultMove(resultId,original,targetProjectId)));
        RefreshSampleIndex(db,tx,original);
        RefreshSampleIndex(db,tx,targetProjectId);
        tx.Commit();
    }

    public string CopyResult(string resultId,string targetProjectId,string? newName=null)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT name FROM experiment_results WHERE id=$id AND is_deleted=0";
        cmd.Parameters.AddWithValue("$id",resultId);
        var original=cmd.ExecuteScalar() as string ?? throw new KeyNotFoundException("源结果单元不存在。");
        var target=CreateResult(targetProjectId,newName??original+" - 副本");
        try
        {
            CopyAssets(ListAssets(resultId).Select(x=>x.Id),target);
            return target;
        }
        catch
        {
            // Roll back the newly created empty logical result on failed copy.
            using var remove=db.CreateCommand();
            remove.CommandText="DELETE FROM experiment_results WHERE id=$id AND NOT EXISTS(SELECT 1 FROM experiment_assets WHERE result_id=$id)";
            remove.Parameters.AddWithValue("$id",target);
            remove.ExecuteNonQuery();
            throw;
        }
    }

    public void SetResultDeleted(string resultId,bool deleted)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        if(deleted)
        {
            using var check=db.CreateCommand();
            check.Transaction=tx;
            check.CommandText="""
                SELECT COUNT(*) FROM experiment_usages u JOIN experiment_assets a ON a.id=u.asset_id
                WHERE a.result_id=$id AND u.state='confirmed'
                """;
            check.Parameters.AddWithValue("$id",resultId);
            if(Convert.ToInt64(check.ExecuteScalar())>0)
                throw new InvalidOperationException("结果单元中存在已确认的论文用途，禁止删除。");
        }
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="UPDATE experiment_results SET is_deleted=$value WHERE id=$id";
        cmd.Parameters.AddWithValue("$value",deleted?1:0);
        cmd.Parameters.AddWithValue("$id",resultId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("结果单元不存在。");
        RefreshSampleIndexForResults(db,tx,[resultId]);
        tx.Commit();
    }

    public void SetProjectDeleted(string projectId,bool deleted)
    {
        using var db=Connect();
        if(deleted)
        {
            using var check=db.CreateCommand();
            check.CommandText="""
                SELECT COUNT(*) FROM experiment_usages u
                JOIN experiment_assets a ON a.id=u.asset_id
                JOIN experiment_results r ON r.id=a.result_id
                WHERE r.project_id=$id AND u.state='confirmed'
                """;
            check.Parameters.AddWithValue("$id",projectId);
            if(Convert.ToInt64(check.ExecuteScalar())>0)
                throw new InvalidOperationException("实验项目中存在已确认的论文用途，禁止删除。");
        }
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE experiment_projects SET is_deleted=$value WHERE id=$id";
        cmd.Parameters.AddWithValue("$value",deleted?1:0);
        cmd.Parameters.AddWithValue("$id",projectId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("实验项目不存在。");
    }

    public IReadOnlyList<ExperimentResult> ListDeletedResults(string projectId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT r.id,r.project_id,r.name,COUNT(a.id)
            FROM experiment_results r LEFT JOIN experiment_assets a ON a.result_id=r.id
            WHERE r.project_id=$id AND r.is_deleted=1 GROUP BY r.id ORDER BY r.imported_at DESC
            """;
        cmd.Parameters.AddWithValue("$id",projectId);
        using var rd=cmd.ExecuteReader();
        var list=new List<ExperimentResult>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),rd.GetInt32(3)));
        return list;
    }

    public IReadOnlyList<ExperimentProject> ListDeletedProjects(string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,name FROM experiment_projects WHERE paper_id=$paper AND is_deleted=1 ORDER BY name";
        cmd.Parameters.AddWithValue("$paper",paperId);
        using var rd=cmd.ExecuteReader();
        var list=new List<ExperimentProject>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1)));
        return list;
    }
}
