using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record ExperimentUnit(string Id,string ProjectId,string Name,int SampleCount,int AssetCount);
public sealed record SampleUnit(string Id,string ExperimentId,string ExperimentName,
    string ProjectId,string SampleName,int RawCount,int ProcessCount,int ShowCount,
    int QuantCount,int OtherCount);
public sealed record SampleUnitAsset(string AssetId,string FileName,string Stage,
    string? ProcessSubtype,int FileVersion,string RelativePath,string Sha256,
    string ResultId,string ParseStatus);
public sealed record CustomTag(string Id,string Name);
public sealed record EntityAnnotation(string Note,IReadOnlyList<CustomTag> Tags);

public sealed partial class ExperimentWorkspace
{
    private void InitializeSampleUnits()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS experiment_units(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES experiment_projects(id),
                name TEXT NOT NULL COLLATE NOCASE,
                created_at TEXT NOT NULL,
                UNIQUE(project_id,name)
            );
            CREATE TABLE IF NOT EXISTS sample_units(
                id TEXT PRIMARY KEY,
                experiment_id TEXT NOT NULL REFERENCES experiment_units(id),
                sample_name TEXT NOT NULL COLLATE NOCASE,
                created_at TEXT NOT NULL,
                UNIQUE(experiment_id,sample_name)
            );
            CREATE TABLE IF NOT EXISTS sample_unit_assets(
                asset_id TEXT PRIMARY KEY REFERENCES experiment_assets(id),
                sample_unit_id TEXT NOT NULL REFERENCES sample_units(id)
            );
            CREATE INDEX IF NOT EXISTS ix_sample_unit_assets_unit
                ON sample_unit_assets(sample_unit_id);
            CREATE TABLE IF NOT EXISTS user_custom_tags(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS user_tag_assignments(
                entity_kind TEXT NOT NULL CHECK(entity_kind IN ('experiment','sample','asset')),
                entity_id TEXT NOT NULL,
                tag_id TEXT NOT NULL REFERENCES user_custom_tags(id),
                PRIMARY KEY(entity_kind,entity_id,tag_id)
            );
            CREATE INDEX IF NOT EXISTS ix_tag_assignments_tag
                ON user_tag_assignments(tag_id,entity_kind,entity_id);
            CREATE TABLE IF NOT EXISTS user_entity_notes(
                entity_kind TEXT NOT NULL CHECK(entity_kind IN ('experiment','sample','asset')),
                entity_id TEXT NOT NULL,
                content TEXT NOT NULL,
                edited_at TEXT NOT NULL,
                PRIMARY KEY(entity_kind,entity_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Materialize only confident, parsed sample memberships. Existing unit IDs
    /// survive rename, deletion/restore and index rebuild; empty units remain
    /// available for their saved notes/tags. No provenance edges are inserted.
    /// </summary>
    private static void RefreshSampleUnits(SqliteConnection db,SqliteTransaction tx,string projectId)
    {
        using var read=db.CreateCommand();
        read.Transaction=tx;
        read.CommandText="""
            SELECT t.asset_id,t.experiment_name,t.sample_name
            FROM asset_system_tags t
            JOIN experiment_assets a ON a.id=t.asset_id
            JOIN experiment_results r ON r.id=a.result_id
            WHERE r.project_id=$project AND a.is_deleted=0 AND r.is_deleted=0
              AND t.parse_status='resolved' AND t.sample_name IS NOT NULL
            ORDER BY t.experiment_name COLLATE NOCASE,t.sample_name COLLATE NOCASE,t.asset_id
            """;
        read.Parameters.AddWithValue("$project",projectId);
        var rows=new List<(string Asset,string Experiment,string Sample)>();
        using(var rd=read.ExecuteReader())
            while(rd.Read())rows.Add((rd.GetString(0),rd.GetString(1),rd.GetString(2)));
        using(var cleanup=db.CreateCommand())
        {
            cleanup.Transaction=tx;
            cleanup.CommandText="""
                DELETE FROM sample_unit_assets WHERE asset_id IN(
                  SELECT a.id FROM experiment_assets a
                  JOIN experiment_results r ON r.id=a.result_id
                  WHERE r.project_id=$project)
                """;
            cleanup.Parameters.AddWithValue("$project",projectId);
            cleanup.ExecuteNonQuery();
        }
        var experiments=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var samples=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows)
        {
            if(!experiments.TryGetValue(row.Experiment,out var experimentId))
            {
                experimentId=LookupOrCreate(db,tx,
                    "experiment_units","project_id","name",projectId,row.Experiment);
                experiments.Add(row.Experiment,experimentId);
            }
            var key=experimentId+"\u001f"+row.Sample;
            if(!samples.TryGetValue(key,out var sampleId))
            {
                sampleId=LookupOrCreate(db,tx,
                    "sample_units","experiment_id","sample_name",experimentId,row.Sample);
                samples.Add(key,sampleId);
            }
            using var add=db.CreateCommand();
            add.Transaction=tx;
            add.CommandText="""
                INSERT INTO sample_unit_assets(asset_id,sample_unit_id)
                VALUES($asset,$sample)
                """;
            add.Parameters.AddWithValue("$asset",row.Asset);
            add.Parameters.AddWithValue("$sample",sampleId);
            add.ExecuteNonQuery();
        }
    }

    private static string LookupOrCreate(SqliteConnection db,SqliteTransaction tx,
        string table,string ownerColumn,string nameColumn,string owner,string name)
    {
        // table and columns come from constant call sites, never from user text.
        using(var existing=db.CreateCommand())
        {
            existing.Transaction=tx;
            existing.CommandText=$"SELECT id FROM {table} WHERE {ownerColumn}=$owner AND {nameColumn}=$name";
            existing.Parameters.AddWithValue("$owner",owner);
            existing.Parameters.AddWithValue("$name",name);
            if(existing.ExecuteScalar() is string id)return id;
        }
        var created=NewId();
        using(var add=db.CreateCommand())
        {
            add.Transaction=tx;
            add.CommandText=$"""
                INSERT INTO {table}(id,{ownerColumn},{nameColumn},created_at)
                VALUES($id,$owner,$name,$now)
                ON CONFLICT({ownerColumn},{nameColumn}) DO NOTHING
                """;
            add.Parameters.AddWithValue("$id",created);
            add.Parameters.AddWithValue("$owner",owner);
            add.Parameters.AddWithValue("$name",name);
            add.Parameters.AddWithValue("$now",UtcNow());
            add.ExecuteNonQuery();
        }
        using var lookup=db.CreateCommand();
        lookup.Transaction=tx;
        lookup.CommandText=$"SELECT id FROM {table} WHERE {ownerColumn}=$owner AND {nameColumn}=$name";
        lookup.Parameters.AddWithValue("$owner",owner);
        lookup.Parameters.AddWithValue("$name",name);
        return lookup.ExecuteScalar() as string
            ??throw new InvalidOperationException("无法创建稳定实验或样本单元。");
    }

    public IReadOnlyList<ExperimentUnit> ListExperimentUnits(string projectId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT e.id,e.project_id,e.name,
              COUNT(DISTINCT CASE WHEN a.id IS NOT NULL THEN s.id END),
              COUNT(a.id)
            FROM experiment_units e
            LEFT JOIN sample_units s ON s.experiment_id=e.id
            LEFT JOIN sample_unit_assets m ON m.sample_unit_id=s.id
            LEFT JOIN experiment_assets a ON a.id=m.asset_id AND a.is_deleted=0
            LEFT JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            WHERE e.project_id=$project
            GROUP BY e.id ORDER BY e.name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$project",projectId);
        var result=new List<ExperimentUnit>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetInt32(3),rd.GetInt32(4)));
        return result;
    }

    public IReadOnlyList<SampleUnit> ListSampleUnits(string? projectId=null,
        string? experimentId=null)
    {
        if(projectId is null&&experimentId is null)
            throw new ArgumentException("必须限定实验或项目范围。");
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT s.id,e.id,e.name,e.project_id,s.sample_name,
              SUM(CASE WHEN a.stage='raw' THEN 1 ELSE 0 END),
              SUM(CASE WHEN a.stage='process' THEN 1 ELSE 0 END),
              SUM(CASE WHEN a.stage='show' THEN 1 ELSE 0 END),
              SUM(CASE WHEN a.stage='quant' THEN 1 ELSE 0 END),
              SUM(CASE WHEN a.stage NOT IN ('raw','process','show','quant')
                    AND a.id IS NOT NULL THEN 1 ELSE 0 END)
            FROM sample_units s
            JOIN experiment_units e ON e.id=s.experiment_id
            JOIN sample_unit_assets m ON m.sample_unit_id=s.id
            JOIN experiment_assets a ON a.id=m.asset_id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN experiment_projects p ON p.id=e.project_id AND p.is_deleted=0
            WHERE ($project IS NULL OR e.project_id=$project)
              AND ($experiment IS NULL OR e.id=$experiment)
            GROUP BY s.id
            ORDER BY e.name COLLATE NOCASE,s.sample_name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$project",(object?)projectId??DBNull.Value);
        cmd.Parameters.AddWithValue("$experiment",(object?)experimentId??DBNull.Value);
        var result=new List<SampleUnit>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.GetString(3),rd.GetString(4),rd.GetInt32(5),rd.GetInt32(6),
            rd.GetInt32(7),rd.GetInt32(8),rd.GetInt32(9)));
        return result;
    }

    public IReadOnlyList<SampleUnitAsset> ListSampleUnitAssets(string sampleUnitId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.filename,t.stage,t.process_subtype,t.file_version,
                a.relative_path,a.sha256,a.result_id,t.parse_status
            FROM sample_unit_assets m
            JOIN experiment_assets a ON a.id=m.asset_id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN asset_system_tags t ON t.asset_id=a.id
            WHERE m.sample_unit_id=$sample
            ORDER BY CASE t.stage
                WHEN 'raw' THEN 0 WHEN 'process' THEN 1
                WHEN 'show' THEN 2 WHEN 'quant' THEN 3 ELSE 4 END,
                a.filename COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$sample",sampleUnitId);
        var result=new List<SampleUnitAsset>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.IsDBNull(3)?null:rd.GetString(3),rd.GetInt32(4),rd.GetString(5),
            rd.GetString(6),rd.GetString(7),rd.GetString(8)));
        return result;
    }

    public SampleUnit? GetSampleUnitForAsset(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT s.id FROM sample_unit_assets m
            JOIN sample_units s ON s.id=m.sample_unit_id
            WHERE m.asset_id=$id
            """;
        cmd.Parameters.AddWithValue("$id",assetId);
        if(cmd.ExecuteScalar() is not string id)return null;
        // Resolve with the same active-only counts as the sample overview.
        using var find=db.CreateCommand();
        find.CommandText="""
            SELECT e.project_id,e.id FROM sample_units s
            JOIN experiment_units e ON e.id=s.experiment_id WHERE s.id=$id
            """;
        find.Parameters.AddWithValue("$id",id);
        using var rd=find.ExecuteReader();
        if(!rd.Read())return null;
        var project=rd.GetString(0);
        var experiment=rd.GetString(1);
        rd.Close();
        return ListSampleUnits(project,experiment).FirstOrDefault(x=>x.Id==id);
    }

    private static string CheckedEntityKind(string kind) =>
        kind is "experiment" or "sample" or "asset"?kind:
            throw new ArgumentException("仅支持实验、样本或素材标签。",nameof(kind));

    private static void RequireEntity(SqliteConnection db,string kind,string id)
    {
        var table=kind switch
        {
            "experiment"=>"experiment_units",
            "sample"=>"sample_units",
            "asset"=>"experiment_assets",
            _=>throw new ArgumentException("不支持的实体类型。")
        };
        using var cmd=db.CreateCommand();
        cmd.CommandText=$"SELECT 1 FROM {table} WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",id);
        if(cmd.ExecuteScalar() is null)throw new KeyNotFoundException("标签目标不存在。");
    }

    public IReadOnlyList<CustomTag> ListCustomTags()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,name FROM user_custom_tags ORDER BY name COLLATE NOCASE";
        var result=new List<CustomTag>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1)));
        return result;
    }

    public CustomTag CreateCustomTag(string name)
    {
        name=name.Trim();
        if(name.Length is <1 or >80 || name.Contains('\n') || name.Contains('\r'))
            throw new ArgumentException("自定义标签名称需为 1–80 个字符且不能换行。");
        using var db=Connect();
        var id=NewId();
        using(var insert=db.CreateCommand())
        {
            insert.CommandText="""
                INSERT INTO user_custom_tags(id,name,created_at)
                VALUES($id,$name,$now)
                ON CONFLICT(name) DO NOTHING
                """;
            insert.Parameters.AddWithValue("$id",id);
            insert.Parameters.AddWithValue("$name",name);
            insert.Parameters.AddWithValue("$now",UtcNow());
            insert.ExecuteNonQuery();
        }
        using var lookup=db.CreateCommand();
        lookup.CommandText="SELECT id,name FROM user_custom_tags WHERE name=$name";
        lookup.Parameters.AddWithValue("$name",name);
        using var rd=lookup.ExecuteReader();
        if(!rd.Read())throw new InvalidOperationException("无法建立自定义标签。");
        return new(rd.GetString(0),rd.GetString(1));
    }

    public void SetEntityTag(string entityKind,string entityId,string tagId,bool selected)
    {
        var kind=CheckedEntityKind(entityKind);
        using var db=Connect();
        using var tx=db.BeginTransaction();
        RequireEntity(db,kind,entityId);
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText=selected?"""
            INSERT OR IGNORE INTO user_tag_assignments(entity_kind,entity_id,tag_id)
            SELECT $kind,$entity,$tag WHERE EXISTS
                (SELECT 1 FROM user_custom_tags WHERE id=$tag)
            """:"""
            DELETE FROM user_tag_assignments WHERE
              entity_kind=$kind AND entity_id=$entity AND tag_id=$tag
            """;
        cmd.Parameters.AddWithValue("$kind",kind);
        cmd.Parameters.AddWithValue("$entity",entityId);
        cmd.Parameters.AddWithValue("$tag",tagId);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void SetEntityNote(string entityKind,string entityId,string note)
    {
        var kind=CheckedEntityKind(entityKind);
        if(note.Length>20000)throw new ArgumentException("备注最多 20000 字。");
        using var db=Connect();
        RequireEntity(db,kind,entityId);
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            INSERT INTO user_entity_notes(entity_kind,entity_id,content,edited_at)
            VALUES($kind,$entity,$note,$now)
            ON CONFLICT(entity_kind,entity_id) DO UPDATE SET
                content=excluded.content,edited_at=excluded.edited_at
            """;
        cmd.Parameters.AddWithValue("$kind",kind);
        cmd.Parameters.AddWithValue("$entity",entityId);
        cmd.Parameters.AddWithValue("$note",note);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
    }

    public EntityAnnotation GetEntityAnnotation(string entityKind,string entityId)
    {
        var kind=CheckedEntityKind(entityKind);
        using var db=Connect();
        RequireEntity(db,kind,entityId);
        using var note=db.CreateCommand();
        note.CommandText="""
            SELECT content FROM user_entity_notes
            WHERE entity_kind=$kind AND entity_id=$entity
            """;
        note.Parameters.AddWithValue("$kind",kind);
        note.Parameters.AddWithValue("$entity",entityId);
        var content=note.ExecuteScalar() as string??"";
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT t.id,t.name FROM user_tag_assignments a
            JOIN user_custom_tags t ON t.id=a.tag_id
            WHERE a.entity_kind=$kind AND a.entity_id=$entity
            ORDER BY t.name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$kind",kind);
        cmd.Parameters.AddWithValue("$entity",entityId);
        var tags=new List<CustomTag>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())tags.Add(new(rd.GetString(0),rd.GetString(1)));
        return new(content,tags);
    }
}
