using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record E1LegacyTagProposal(string AssetId,string FileName,
    string ExperimentName,string? SampleName,string Stage,
    string? ProcessSubtype,int FileVersion,string ParseStatus,string SourceKind);
public sealed record E1LegacyMigrationPreview(string ProjectId,string Fingerprint,
    IReadOnlyList<E1LegacyTagProposal> Proposals,int Resolved,int Unresolved,
    int LegacyFallback);
public sealed record E1LegacyMigrationResult(string SessionId,int TagsAdded,
    string BackupPath);

public sealed partial class ExperimentWorkspace
{
    private sealed record E1Membership(string AssetId,string SampleUnitId);
    private sealed record E1UndoState(string[] InsertedTags,
        E1Membership[] BeforeMembership,string[] NewSamples,string[] NewExperiments);

    private void InitializeE1LegacyMigration()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS e1_legacy_migration_sessions(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES experiment_projects(id),
                before_hash TEXT NOT NULL,
                after_hash TEXT NOT NULL,
                undo_json TEXT NOT NULL,
                backup_path TEXT NOT NULL,
                applied_at TEXT NOT NULL,
                undone_at TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static E1LegacyMigrationPreview PlanE1LegacyMigration(
        SqliteConnection db,SqliteTransaction tx,string projectId)
    {
        using var check=db.CreateCommand();
        check.Transaction=tx;
        check.CommandText="""
            SELECT COUNT(*) FROM experiment_projects
            WHERE id=$project AND is_deleted=0
            """;
        check.Parameters.AddWithValue("$project",projectId);
        if(Convert.ToInt64(check.ExecuteScalar())!=1)
            throw new KeyNotFoundException("目标实验项目不存在。");
        using var query=db.CreateCommand();
        query.Transaction=tx;
        query.CommandText="""
            SELECT a.id,a.filename,a.relative_path,a.stage,a.result_id,
                r.name,labels.experiment_name,labels.source_kind,
                t.asset_id
            FROM experiment_assets a
            JOIN experiment_results r ON r.id=a.result_id
            LEFT JOIN experiment_result_labels labels ON labels.result_id=r.id
            LEFT JOIN asset_system_tags t ON t.asset_id=a.id
            WHERE r.project_id=$project AND a.is_deleted=0 AND r.is_deleted=0
            ORDER BY a.result_id,a.id
            """;
        query.Parameters.AddWithValue("$project",projectId);
        var rows=new List<(string Id,string Name,string Path,string Stage,
            string Result,string ResultName,string? Label,string? Kind,bool Tagged)>();
        using(var rd=query.ExecuteReader())
            while(rd.Read())
                rows.Add((rd.GetString(0),rd.GetString(1),rd.GetString(2),
                    rd.GetString(3),rd.GetString(4),rd.GetString(5),
                    rd.IsDBNull(6)?null:rd.GetString(6),
                    rd.IsDBNull(7)?null:rd.GetString(7),!rd.IsDBNull(8)));

        var proposals=new List<E1LegacyTagProposal>();
        foreach(var batch in rows.GroupBy(x=>x.Result,StringComparer.Ordinal))
        {
            // Anchors cannot leak across results. Two old results with a
            // matching sample prefix are not necessarily the same experiment.
            var anchors=batch.Select(x=>ParseAssetName(x.Name).SampleName)
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .Select(x=>x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x=>x.Length)
                .ThenBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
            foreach(var item in batch.Where(x=>!x.Tagged))
            {
                var stem=Path.GetFileNameWithoutExtension(item.Name);
                var match=anchors.FirstOrDefault(x=>
                    stem.StartsWith(x+"_",StringComparison.OrdinalIgnoreCase)||
                    stem.StartsWith(x+"-",StringComparison.OrdinalIgnoreCase));
                var parsed=ParseAssetName(item.Name,match);
                var stage=parsed.Stage=="other"?item.Stage:parsed.Stage;
                var sample=parsed.SampleName;
                var subtype=stage=="process"?parsed.ProcessSubtype:null;
                var kind=item.Kind??"legacy_fallback";
                // A result without a recorded physical import root is an
                // independent provisional experiment. Never merge old results
                // solely because they share a result display name.
                var experiment=item.Label??item.ResultName+
                    " [legacy-"+item.Result[..Math.Min(item.Result.Length,8)]+"]";
                var resolved=sample is not null &&
                    stage is "raw" or "process" or "show" or "quant";
                var status=resolved?"resolved":"unresolved";
                var folderStage=item.Path.Replace('\\','/').Split('/')
                    .SkipLast(1).Select(x=>x.ToLowerInvariant() switch
                    {
                        "raw_data"=>"raw","process_data"=>"process",
                        "show_data"=>"show","quant_data"=>"quant",_=>null
                    }).FirstOrDefault(x=>x is not null);
                if(parsed.Stage!="other"&&folderStage is not null&&
                    folderStage!=stage)status="conflict";
                if(parsed.Stage!="other"&&item.Stage is "raw" or "process" or "show" or "quant"
                    && parsed.Stage!=item.Stage)status="conflict";
                proposals.Add(new(item.Id,item.Name,experiment,sample,stage,
                    subtype,parsed.FileVersion,status,kind));
            }
        }
        var fingerprint=ComputeE0Fingerprint(db,tx);
        return new(projectId,fingerprint,proposals,
            proposals.Count(x=>x.ParseStatus=="resolved"),
            proposals.Count(x=>x.ParseStatus!="resolved"),
            proposals.Count(x=>x.SourceKind=="legacy_fallback"));
    }

    public E1LegacyMigrationPreview PreviewE1LegacyMigration(string projectId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction(deferred:true);
        var plan=PlanE1LegacyMigration(db,tx,projectId);
        tx.Commit();
        return plan;
    }

    /// <summary>
    /// Back up SQLite in a consistent snapshot before any E1 changes. The
    /// destination must be an empty external backup directory.
    /// </summary>
    public E1LegacyMigrationResult ApplyE1LegacyMigration(string projectId,
        string expectedFingerprint,string backupDirectory)
    {
        var target=Path.GetFullPath(backupDirectory);
        if(target.Equals(_root,StringComparison.OrdinalIgnoreCase)||
            target.StartsWith(_root+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("E1 迁移备份必须保存在数据仓库之外。");
        Directory.CreateDirectory(target);
        var file=Path.Combine(target,"workspace-e1-"+DateTime.UtcNow
            .ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".db");
        // Backup uses SQLite backup API (safe with WAL), never File.Copy of
        // the live database. No workspace changes occur if backup fails.
        using(var source=Connect())
        using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=file,Mode=SqliteOpenMode.ReadWriteCreate,
            Pooling=false
        }.ToString()))
        {
            backup.Open();
            source.BackupDatabase(backup);
        }

        using var db=Connect();
        using var tx=db.BeginTransaction();
        var plan=PlanE1LegacyMigration(db,tx,projectId);
        if(plan.Fingerprint!=expectedFingerprint)
            throw new InvalidOperationException(
                "数据已改变，请重新预览 E1 标签迁移；旧数据未修改。");
        var beforeMembers=ReadE1Membership(db,tx,projectId);
        var beforeSamples=ReadE1Ids(db,tx,"sample_units",projectId);
        var beforeExperiments=ReadE1Ids(db,tx,"experiment_units",projectId);
        foreach(var tag in plan.Proposals)
        {
            using var insert=db.CreateCommand();
            insert.Transaction=tx;
            insert.CommandText="""
                INSERT INTO asset_system_tags(asset_id,experiment_name,sample_name,
                    stage,process_subtype,file_version,parse_status,source_kind,parser_version)
                VALUES($id,$experiment,$sample,$stage,$subtype,$version,
                    $status,$source,$parser)
                """;
            insert.Parameters.AddWithValue("$id",tag.AssetId);
            insert.Parameters.AddWithValue("$experiment",tag.ExperimentName);
            insert.Parameters.AddWithValue("$sample",(object?)tag.SampleName??DBNull.Value);
            insert.Parameters.AddWithValue("$stage",tag.Stage);
            insert.Parameters.AddWithValue("$subtype",(object?)tag.ProcessSubtype??DBNull.Value);
            insert.Parameters.AddWithValue("$version",tag.FileVersion);
            insert.Parameters.AddWithValue("$status",tag.ParseStatus);
            insert.Parameters.AddWithValue("$source",tag.SourceKind);
            insert.Parameters.AddWithValue("$parser",SystemTagParserVersion);
            insert.ExecuteNonQuery();
        }
        if(plan.Proposals.Count>0)RefreshSampleUnits(db,tx,projectId);
        var undo=new E1UndoState(
            plan.Proposals.Select(x=>x.AssetId).ToArray(),beforeMembers,
            ReadE1Ids(db,tx,"sample_units",projectId)
                .Except(beforeSamples,StringComparer.Ordinal).ToArray(),
            ReadE1Ids(db,tx,"experiment_units",projectId)
                .Except(beforeExperiments,StringComparer.Ordinal).ToArray());
        var after=ComputeE0Fingerprint(db,tx);
        var id=NewId();
        using(var journal=db.CreateCommand())
        {
            journal.Transaction=tx;
            journal.CommandText="""
                INSERT INTO e1_legacy_migration_sessions(
                    id,project_id,before_hash,after_hash,undo_json,
                    backup_path,applied_at)
                VALUES($id,$project,$before,$after,$undo,$backup,$now)
                """;
            journal.Parameters.AddWithValue("$id",id);
            journal.Parameters.AddWithValue("$project",projectId);
            journal.Parameters.AddWithValue("$before",plan.Fingerprint);
            journal.Parameters.AddWithValue("$after",after);
            journal.Parameters.AddWithValue("$undo",JsonSerializer.Serialize(undo));
            journal.Parameters.AddWithValue("$backup",file);
            journal.Parameters.AddWithValue("$now",UtcNow());
            journal.ExecuteNonQuery();
        }
        tx.Commit();
        return new(id,plan.Proposals.Count,file);
    }

    private static E1Membership[] ReadE1Membership(SqliteConnection db,
        SqliteTransaction tx,string projectId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT m.asset_id,m.sample_unit_id
            FROM sample_unit_assets m
            JOIN experiment_assets a ON a.id=m.asset_id
            JOIN experiment_results r ON r.id=a.result_id
            WHERE r.project_id=$project ORDER BY m.asset_id
            """;
        cmd.Parameters.AddWithValue("$project",projectId);
        var result=new List<E1Membership>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1)));
        return result.ToArray();
    }
    private static string[] ReadE1Ids(SqliteConnection db,SqliteTransaction tx,
        string table,string projectId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        // Table is a compile-time constant from the two caller sites.
        cmd.CommandText=table=="sample_units"?
            "SELECT s.id FROM sample_units s JOIN experiment_units e ON e.id=s.experiment_id WHERE e.project_id=$project":
            "SELECT id FROM experiment_units WHERE project_id=$project";
        cmd.Parameters.AddWithValue("$project",projectId);
        var result=new List<string>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(rd.GetString(0));
        return result.ToArray();
    }

    public bool UndoE1LegacyMigration(string sessionId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var find=db.CreateCommand();
        find.Transaction=tx;
        find.CommandText="""
            SELECT project_id,after_hash,undo_json
            FROM e1_legacy_migration_sessions
            WHERE id=$id AND undone_at IS NULL
            """;
        find.Parameters.AddWithValue("$id",sessionId);
        string project,after;
        E1UndoState undo;
        using(var rd=find.ExecuteReader())
        {
            if(!rd.Read())return false;
            project=rd.GetString(0);
            after=rd.GetString(1);
            undo=JsonSerializer.Deserialize<E1UndoState>(rd.GetString(2))
                ??throw new InvalidOperationException("迁移撤销记录不完整。");
        }
        if(ComputeE0Fingerprint(db,tx)!=after)
            throw new InvalidOperationException(
                "迁移后已有其他数据操作；禁止覆盖新内容。请从备份进行受控恢复。");
        using(var remove=db.CreateCommand())
        {
            remove.Transaction=tx;
            remove.CommandText="""
                DELETE FROM sample_unit_assets WHERE asset_id IN(
                    SELECT a.id FROM experiment_assets a
                    JOIN experiment_results r ON r.id=a.result_id
                    WHERE r.project_id=$project)
                """;
            remove.Parameters.AddWithValue("$project",project);
            remove.ExecuteNonQuery();
        }
        foreach(var item in undo.BeforeMembership)
        {
            using var add=db.CreateCommand();
            add.Transaction=tx;
            add.CommandText="INSERT INTO sample_unit_assets(asset_id,sample_unit_id) VALUES($asset,$sample)";
            add.Parameters.AddWithValue("$asset",item.AssetId);
            add.Parameters.AddWithValue("$sample",item.SampleUnitId);
            add.ExecuteNonQuery();
        }
        foreach(var assetId in undo.InsertedTags)
        {
            using var delete=db.CreateCommand();
            delete.Transaction=tx;
            delete.CommandText="DELETE FROM asset_system_tags WHERE asset_id=$id";
            delete.Parameters.AddWithValue("$id",assetId);
            delete.ExecuteNonQuery();
        }
        foreach(var sampleId in undo.NewSamples)
        {
            using var delete=db.CreateCommand();
            delete.Transaction=tx;
            delete.CommandText="DELETE FROM sample_units WHERE id=$id";
            delete.Parameters.AddWithValue("$id",sampleId);
            delete.ExecuteNonQuery();
        }
        foreach(var experimentId in undo.NewExperiments)
        {
            using var delete=db.CreateCommand();
            delete.Transaction=tx;
            delete.CommandText="DELETE FROM experiment_units WHERE id=$id";
            delete.Parameters.AddWithValue("$id",experimentId);
            delete.ExecuteNonQuery();
        }
        using(var finish=db.CreateCommand())
        {
            finish.Transaction=tx;
            finish.CommandText="UPDATE e1_legacy_migration_sessions SET undone_at=$now WHERE id=$id";
            finish.Parameters.AddWithValue("$now",UtcNow());
            finish.Parameters.AddWithValue("$id",sessionId);
            finish.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }
}
