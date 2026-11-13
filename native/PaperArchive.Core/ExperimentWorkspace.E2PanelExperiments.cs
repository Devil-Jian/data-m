using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record E2PanelExperimentLink(
    string PanelId,string ExperimentId,string ExperimentName,
    string FigureId,int FigureNumber,string PanelLabel);
public sealed record E2PanelExperimentChange(
    string? ChangeId,string PanelId,string? BeforeExperimentId,string? AfterExperimentId);
public sealed record E2MigrationCandidate(
    string PanelId,string FigureId,int FigureNumber,string PanelLabel,
    string? ExperimentId,string? ExperimentName,string Decision);
public sealed record E2MigrationPreview(
    string Fingerprint,IReadOnlyList<E2MigrationCandidate> Candidates,
    int ReadyCount,int SkippedCount);
public sealed record E2MigrationResult(
    string SessionId,int LinkedCount,int SkippedCount,string BackupPath);

/// <summary>
/// E2: one experiment is bound to at most one Panel, and each Panel has at
/// most one experiment. Old D3 sample/SHOW and archived snapshots are never
/// deleted or rewritten; migration only adds new current-workspace links.
/// </summary>
public sealed partial class ExperimentWorkspace
{
    private void InitializeE2PanelExperiments()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS e2_panel_experiment_links(
                panel_id TEXT PRIMARY KEY REFERENCES panel_templates(id) ON DELETE CASCADE,
                experiment_id TEXT NOT NULL UNIQUE REFERENCES experiment_units(id),
                linked_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_e2_panel_experiment_id
                ON e2_panel_experiment_links(experiment_id);
            CREATE TABLE IF NOT EXISTS e2_panel_experiment_changes(
                id TEXT PRIMARY KEY,
                panel_id TEXT NOT NULL,
                before_experiment_id TEXT,
                after_experiment_id TEXT,
                session_id TEXT,
                changed_at TEXT NOT NULL,
                undone_at TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_e2_changes_panel
                ON e2_panel_experiment_changes(panel_id);
            CREATE TABLE IF NOT EXISTS e2_migration_sessions(
                id TEXT PRIMARY KEY,
                before_fingerprint TEXT NOT NULL,
                after_fingerprint TEXT NOT NULL,
                backup_path TEXT NOT NULL,
                linked_count INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                undone_at TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static string? FindE2Experiment(SqliteConnection db,
        SqliteTransaction tx,string panelId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT experiment_id FROM e2_panel_experiment_links WHERE panel_id=$panel
            """;
        cmd.Parameters.AddWithValue("$panel",panelId);
        return cmd.ExecuteScalar() as string;
    }

    private static void RequireE2Panel(SqliteConnection db,SqliteTransaction tx,string panelId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT 1 FROM panel_templates WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",panelId);
        if(cmd.ExecuteScalar() is null)throw new KeyNotFoundException("Panel 不存在。");
    }

    private static void RequireE2Experiment(SqliteConnection db,SqliteTransaction tx,string id)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT 1 FROM experiment_units e
            JOIN experiment_projects p ON p.id=e.project_id AND p.is_deleted=0
            WHERE e.id=$id
            """;
        cmd.Parameters.AddWithValue("$id",id);
        if(cmd.ExecuteScalar() is null)throw new KeyNotFoundException("实验不存在或已停用。");
    }

    private static void CheckE2Availability(SqliteConnection db,SqliteTransaction tx,
        string panelId,string experimentId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT panel_id FROM e2_panel_experiment_links WHERE experiment_id=$id
            """;
        cmd.Parameters.AddWithValue("$id",experimentId);
        if(cmd.ExecuteScalar() is string occupied && occupied!=panelId)
            throw new InvalidOperationException("该实验已经关联到其他 Panel，不能重复分配。");
    }

    private static void WriteE2Link(SqliteConnection db,SqliteTransaction tx,
        string panelId,string? experimentId)
    {
        if(experimentId is null)
        {
            using var delete=db.CreateCommand();
            delete.Transaction=tx;
            delete.CommandText="DELETE FROM e2_panel_experiment_links WHERE panel_id=$panel";
            delete.Parameters.AddWithValue("$panel",panelId);
            delete.ExecuteNonQuery();
        }
        else
        {
            using var write=db.CreateCommand();
            write.Transaction=tx;
            write.CommandText="""
                INSERT INTO e2_panel_experiment_links(
                    panel_id,experiment_id,linked_at,updated_at)
                VALUES($panel,$experiment,$now,$now)
                ON CONFLICT(panel_id) DO UPDATE SET
                    experiment_id=excluded.experiment_id,
                    updated_at=excluded.updated_at
                """;
            write.Parameters.AddWithValue("$panel",panelId);
            write.Parameters.AddWithValue("$experiment",experimentId);
            write.Parameters.AddWithValue("$now",UtcNow());
            write.ExecuteNonQuery();
        }
    }

    private static string LogE2Change(SqliteConnection db,SqliteTransaction tx,
        string panelId,string? before,string? after,string? session=null)
    {
        var id=NewId();
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO e2_panel_experiment_changes(
                id,panel_id,before_experiment_id,after_experiment_id,session_id,changed_at)
            VALUES($id,$panel,$before,$after,$session,$now)
            """;
        cmd.Parameters.AddWithValue("$id",id);
        cmd.Parameters.AddWithValue("$panel",panelId);
        cmd.Parameters.AddWithValue("$before",(object?)before??DBNull.Value);
        cmd.Parameters.AddWithValue("$after",(object?)after??DBNull.Value);
        cmd.Parameters.AddWithValue("$session",(object?)session??DBNull.Value);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
        return id;
    }

    /// <summary>
    /// Explicitly replace a Panel's experiment. A plain link refuses to
    /// overwrite an existing different experiment; never steal an experiment
    /// already used by another Panel.
    /// </summary>
    public E2PanelExperimentChange LinkPanelExperiment(
        string panelId,string experimentId,bool replace=false)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        RequireE2Panel(db,tx,panelId);
        RequireE2Experiment(db,tx,experimentId);
        var before=FindE2Experiment(db,tx,panelId);
        if(before==experimentId)
        {
            tx.Commit();
            return new(null,panelId,before,experimentId);
        }
        if(before is not null&&!replace)
            throw new InvalidOperationException("该 Panel 已关联实验；必须明确执行替换。");
        CheckE2Availability(db,tx,panelId,experimentId);
        WriteE2Link(db,tx,panelId,experimentId);
        var changeId=LogE2Change(db,tx,panelId,before,experimentId);
        tx.Commit();
        return new(changeId,panelId,before,experimentId);
    }

    public E2PanelExperimentChange UnlinkPanelExperiment(string panelId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        RequireE2Panel(db,tx,panelId);
        var before=FindE2Experiment(db,tx,panelId);
        if(before is null)
        {
            tx.Commit();
            return new(null,panelId,null,null);
        }
        WriteE2Link(db,tx,panelId,null);
        var changeId=LogE2Change(db,tx,panelId,before,null);
        tx.Commit();
        return new(changeId,panelId,before,null);
    }

    private static bool HasLaterE2Change(SqliteConnection db,
        SqliteTransaction tx,string changeId,string panelId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT COUNT(*) FROM e2_panel_experiment_changes
            WHERE panel_id=$panel AND rowid>(
                SELECT rowid FROM e2_panel_experiment_changes WHERE id=$id)
            """;
        cmd.Parameters.AddWithValue("$panel",panelId);
        cmd.Parameters.AddWithValue("$id",changeId);
        return Convert.ToInt64(cmd.ExecuteScalar())>0;
    }

    public bool UndoPanelExperimentChange(string changeId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT panel_id,before_experiment_id,after_experiment_id,session_id
            FROM e2_panel_experiment_changes
            WHERE id=$id AND undone_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$id",changeId);
        string panel;
        string? before,after,session;
        using(var rd=cmd.ExecuteReader())
        {
            if(!rd.Read())return false;
            panel=rd.GetString(0);
            before=rd.IsDBNull(1)?null:rd.GetString(1);
            after=rd.IsDBNull(2)?null:rd.GetString(2);
            session=rd.IsDBNull(3)?null:rd.GetString(3);
        }
        if(session is not null)
            throw new InvalidOperationException("批量迁移需要使用迁移会话撤销。");
        if(HasLaterE2Change(db,tx,changeId,panel) ||
           FindE2Experiment(db,tx,panel)!=after)
            throw new InvalidOperationException("关联已经变化，拒绝覆盖后续操作。");
        if(before is not null)
        {
            RequireE2Experiment(db,tx,before);
            CheckE2Availability(db,tx,panel,before);
        }
        WriteE2Link(db,tx,panel,before);
        using var mark=db.CreateCommand();
        mark.Transaction=tx;
        mark.CommandText="""
            UPDATE e2_panel_experiment_changes SET undone_at=$now WHERE id=$id
            """;
        mark.Parameters.AddWithValue("$id",changeId);
        mark.Parameters.AddWithValue("$now",UtcNow());
        mark.ExecuteNonQuery();
        tx.Commit();
        return true;
    }

    public E2PanelExperimentLink? GetPanelExperiment(string panelId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT l.panel_id,l.experiment_id,e.name,p.figure_id,
                f.number,p.label
            FROM e2_panel_experiment_links l
            JOIN experiment_units e ON e.id=l.experiment_id
            JOIN panel_templates p ON p.id=l.panel_id
            JOIN figure_templates f ON f.id=p.figure_id
            WHERE l.panel_id=$id
            """;
        cmd.Parameters.AddWithValue("$id",panelId);
        using var rd=cmd.ExecuteReader();
        return rd.Read()?new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.GetString(3),rd.GetInt32(4),rd.GetString(5)):null;
    }

    public IReadOnlyList<E2PanelExperimentLink> ListFigurePanelExperiments(string figureId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT l.panel_id,l.experiment_id,e.name,p.figure_id,
                f.number,p.label
            FROM e2_panel_experiment_links l
            JOIN experiment_units e ON e.id=l.experiment_id
            JOIN panel_templates p ON p.id=l.panel_id
            JOIN figure_templates f ON f.id=p.figure_id
            WHERE p.figure_id=$id
            ORDER BY p.label COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$id",figureId);
        var result=new List<E2PanelExperimentLink>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetInt32(4),rd.GetString(5)));
        return result;
    }

    private static string FingerprintE2(SqliteConnection db,SqliteTransaction tx)
    {
        var canonical=new StringBuilder(ComputeE0Fingerprint(db,tx));
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT panel_id,experiment_id FROM e2_panel_experiment_links
            ORDER BY panel_id
            """;
        using var rd=cmd.ExecuteReader();
        while(rd.Read())
            canonical.Append('\n').Append(rd.GetString(0)).Append(':').Append(rd.GetString(1));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// E0's legacy candidates can migrate only when one unambiguous, root-
    /// verified experiment is not already assigned elsewhere. Read-only.
    /// </summary>
    public E2MigrationPreview PreviewE2LegacyMigration()
    {
        var old=PreviewE0Migration();
        using var db=Connect();
        using var tx=db.BeginTransaction(deferred:true);
        var candidates=new List<E2MigrationCandidate>();
        foreach(var candidate in old.Panels)
        {
            string? experiment=candidate.ExperimentIds.Count==1?
                candidate.ExperimentIds[0]:null;
            string? name=candidate.ExperimentNames.Count==1?
                candidate.ExperimentNames[0]:null;
            var decision=candidate.Decision;
            if(decision=="ready"&&experiment is not null)
            {
                using var trusted=db.CreateCommand();
                trusted.Transaction=tx;
                trusted.CommandText="""
                    SELECT 1 FROM e1_experiment_roots
                    WHERE experiment_id=$id
                    """;
                trusted.Parameters.AddWithValue("$id",experiment);
                if(trusted.ExecuteScalar() is null)decision="root_unverified";
                else
                {
                    var current=FindE2Experiment(db,tx,candidate.PanelId);
                    if(current==experiment)decision="already_linked";
                    else if(current is not null)decision="panel_occupied";
                    else
                    {
                        using var occupied=db.CreateCommand();
                        occupied.Transaction=tx;
                        occupied.CommandText="""
                            SELECT 1 FROM e2_panel_experiment_links
                            WHERE experiment_id=$id AND panel_id<>$panel
                            """;
                        occupied.Parameters.AddWithValue("$id",experiment);
                        occupied.Parameters.AddWithValue("$panel",candidate.PanelId);
                        if(occupied.ExecuteScalar() is not null)
                            decision="experiment_occupied";
                    }
                }
            }
            candidates.Add(new(candidate.PanelId,candidate.FigureId,
                candidate.FigureNumber,candidate.PanelLabel,experiment,name,decision));
        }
        var fingerprint=FingerprintE2(db,tx);
        tx.Commit();
        return new(fingerprint,candidates,candidates.Count(x=>x.Decision=="ready"),
            candidates.Count(x=>x.Decision!="ready"));
    }

    public E2MigrationResult ApplyE2LegacyMigration(
        string expectedFingerprint,string backupDirectory)
    {
        var target=Path.GetFullPath(backupDirectory);
        if(target.Equals(_root,StringComparison.OrdinalIgnoreCase)||
            target.StartsWith(_root+Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("迁移备份目录必须位于数据仓库之外。");
        Directory.CreateDirectory(target);
        var backupPath=Path.Combine(target,"workspace-e2-"+DateTime.UtcNow
            .ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".db");
        using(var source=Connect())
        using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource=backupPath,Mode=SqliteOpenMode.ReadWriteCreate,
            Pooling=false
        }.ToString()))
        {
            backup.Open();
            source.BackupDatabase(backup);
        }
        // Guard after the SQLite backup; the preview is rebuilt and its
        // fingerprint verified again before any INSERT or session log write.
        var plan=PreviewE2LegacyMigration();
        if(plan.Fingerprint!=expectedFingerprint)
            throw new InvalidOperationException("数据库已变化，请重新预览迁移。");
        using var db=Connect();
        using var tx=db.BeginTransaction();
        if(FingerprintE2(db,tx)!=expectedFingerprint)
            throw new InvalidOperationException("提交时数据已变化，拒绝执行陈旧迁移。");
        var id=NewId();
        foreach(var candidate in plan.Candidates.Where(x=>x.Decision=="ready"))
        {
            if(candidate.ExperimentId is null)
                throw new InvalidOperationException("迁移候选缺少实验身份。");
            RequireE2Panel(db,tx,candidate.PanelId);
            RequireE2Experiment(db,tx,candidate.ExperimentId);
            if(FindE2Experiment(db,tx,candidate.PanelId) is not null)
                throw new InvalidOperationException("迁移期间 Panel 已被占用。");
            CheckE2Availability(db,tx,candidate.PanelId,candidate.ExperimentId);
            WriteE2Link(db,tx,candidate.PanelId,candidate.ExperimentId);
            LogE2Change(db,tx,candidate.PanelId,null,candidate.ExperimentId,id);
        }
        var after=FingerprintE2(db,tx);
        using(var save=db.CreateCommand())
        {
            save.Transaction=tx;
            save.CommandText="""
                INSERT INTO e2_migration_sessions(
                    id,before_fingerprint,after_fingerprint,backup_path,
                    linked_count,created_at)
                VALUES($id,$before,$after,$backup,$count,$now)
                """;
            save.Parameters.AddWithValue("$id",id);
            save.Parameters.AddWithValue("$before",expectedFingerprint);
            save.Parameters.AddWithValue("$after",after);
            save.Parameters.AddWithValue("$backup",backupPath);
            save.Parameters.AddWithValue("$count",plan.ReadyCount);
            save.Parameters.AddWithValue("$now",UtcNow());
            save.ExecuteNonQuery();
        }
        tx.Commit();
        return new(id,plan.ReadyCount,plan.SkippedCount,backupPath);
    }

    public bool UndoE2LegacyMigration(string sessionId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var session=db.CreateCommand();
        session.Transaction=tx;
        session.CommandText="""
            SELECT after_fingerprint FROM e2_migration_sessions
            WHERE id=$id AND undone_at IS NULL
            """;
        session.Parameters.AddWithValue("$id",sessionId);
        var expected=session.ExecuteScalar() as string;
        if(expected is null)return false;
        if(FingerprintE2(db,tx)!=expected)
            throw new InvalidOperationException(
                "迁移后索引已发生变化，拒绝覆盖后续操作；请使用备份恢复。");
        var changes=new List<(string Id,string Panel,string Experiment)>();
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="""
                SELECT id,panel_id,after_experiment_id
                FROM e2_panel_experiment_changes
                WHERE session_id=$session AND undone_at IS NULL
                ORDER BY rowid DESC
                """;
            cmd.Parameters.AddWithValue("$session",sessionId);
            using var rd=cmd.ExecuteReader();
            while(rd.Read())changes.Add((rd.GetString(0),rd.GetString(1),rd.GetString(2)));
        }
        foreach(var change in changes)
        {
            if(HasLaterE2Change(db,tx,change.Id,change.Panel) ||
               FindE2Experiment(db,tx,change.Panel)!=change.Experiment)
                throw new InvalidOperationException("Panel 已被重新关联，批量撤销被拒绝。");
            WriteE2Link(db,tx,change.Panel,null);
            using var mark=db.CreateCommand();
            mark.Transaction=tx;
            mark.CommandText="""
                UPDATE e2_panel_experiment_changes
                SET undone_at=$now WHERE id=$id
                """;
            mark.Parameters.AddWithValue("$now",UtcNow());
            mark.Parameters.AddWithValue("$id",change.Id);
            mark.ExecuteNonQuery();
        }
        using var finish=db.CreateCommand();
        finish.Transaction=tx;
        finish.CommandText="""
            UPDATE e2_migration_sessions SET undone_at=$now WHERE id=$id
            """;
        finish.Parameters.AddWithValue("$now",UtcNow());
        finish.Parameters.AddWithValue("$id",sessionId);
        finish.ExecuteNonQuery();
        tx.Commit();
        return true;
    }
}
