using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record E1FolderIdentity(string Id,string ExperimentId,string ProjectId,
    string ExperimentName,string RootPath,int ActiveFileCount);
public sealed record E1FolderAsset(string AssetId,string RelativePath,string Sha256);

public sealed partial class ExperimentWorkspace
{
    private void InitializeE1FolderIndex()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS e1_experiment_roots(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES experiment_projects(id),
                experiment_name TEXT NOT NULL COLLATE NOCASE,
                normalized_root TEXT NOT NULL COLLATE NOCASE,
                created_at TEXT NOT NULL,
                UNIQUE(project_id,normalized_root),
                UNIQUE(project_id,experiment_name)
            );
            CREATE TABLE IF NOT EXISTS e1_root_result_batches(
                result_id TEXT PRIMARY KEY REFERENCES experiment_results(id),
                root_id TEXT NOT NULL REFERENCES e1_experiment_roots(id)
            );
            CREATE TABLE IF NOT EXISTS e1_root_current_assets(
                root_id TEXT NOT NULL REFERENCES e1_experiment_roots(id),
                relative_path TEXT NOT NULL COLLATE NOCASE,
                asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                sha256 TEXT NOT NULL,
                PRIMARY KEY(root_id,relative_path)
            );
            CREATE INDEX IF NOT EXISTS ix_e1_root_current_assets_asset
                ON e1_root_current_assets(asset_id);
            """;
        cmd.ExecuteNonQuery();
        AddColumn(db,"e1_experiment_roots","experiment_id",
            "TEXT REFERENCES experiment_units(id)");
    }

    private static string NormalizeE1Root(string path)=>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            .Replace('\\','/');

    private static (string RootId,string ExperimentName) EnsureE1Root(
        SqliteConnection db,SqliteTransaction tx,string projectId,string source)
    {
        var normalized=NormalizeE1Root(source);
        using(var existing=db.CreateCommand())
        {
            existing.Transaction=tx;
            existing.CommandText="""
                SELECT id,experiment_name FROM e1_experiment_roots
                WHERE project_id=$project AND normalized_root=$root
                """;
            existing.Parameters.AddWithValue("$project",projectId);
            existing.Parameters.AddWithValue("$root",normalized);
            using var reader=existing.ExecuteReader();
            if(reader.Read())return(reader.GetString(0),reader.GetString(1));
        }
        var basename=Path.GetFileName(normalized);
        if(string.IsNullOrWhiteSpace(basename))
            throw new InvalidOperationException("实验根文件夹必须有名称。");
        var name=basename;
        for(var suffix=2;;suffix++)
        {
            using var exists=db.CreateCommand();
            exists.Transaction=tx;
            exists.CommandText="""
                SELECT 1 FROM experiment_units WHERE project_id=$project AND name=$name
                UNION
                SELECT 1 FROM e1_experiment_roots WHERE project_id=$project AND experiment_name=$name
                """;
            exists.Parameters.AddWithValue("$project",projectId);
            exists.Parameters.AddWithValue("$name",name);
            if(exists.ExecuteScalar() is null)break;
            name=basename+" ("+suffix+")";
        }
        var id=NewId();
        using var add=db.CreateCommand();
        add.Transaction=tx;
        add.CommandText="""
            INSERT INTO e1_experiment_roots(id,project_id,experiment_name,normalized_root,created_at)
            VALUES($id,$project,$name,$root,$now)
            """;
        add.Parameters.AddWithValue("$id",id);
        add.Parameters.AddWithValue("$project",projectId);
        add.Parameters.AddWithValue("$name",name);
        add.Parameters.AddWithValue("$root",normalized);
        add.Parameters.AddWithValue("$now",UtcNow());
        add.ExecuteNonQuery();
        return(id,name);
    }

    private static string BindE1RootExperiment(SqliteConnection db,
        SqliteTransaction tx,string rootId,string projectId,string name)
    {
        var experimentId=LookupOrCreate(db,tx,
            "experiment_units","project_id","name",projectId,name);
        using var update=db.CreateCommand();
        update.Transaction=tx;
        update.CommandText="""
            UPDATE e1_experiment_roots SET experiment_id=$experiment
            WHERE id=$root AND (experiment_id IS NULL OR experiment_id=$experiment)
            """;
        update.Parameters.AddWithValue("$root",rootId);
        update.Parameters.AddWithValue("$experiment",experimentId);
        if(update.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("实验文件夹映射的 Experiment ID 已发生冲突。");
        return experimentId;
    }

    private static Dictionary<string,(string Asset,string Sha)> ReadE1Current(
        SqliteConnection db,SqliteTransaction tx,string rootId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT current.relative_path,current.asset_id,current.sha256
            FROM e1_root_current_assets current
            JOIN experiment_assets a ON a.id=current.asset_id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN e1_root_result_batches batch ON batch.result_id=a.result_id
                AND batch.root_id=current.root_id
            WHERE current.root_id=$id
            """;
        cmd.Parameters.AddWithValue("$id",rootId);
        var dict=new Dictionary<string,(string Asset,string Sha)>(
            StringComparer.OrdinalIgnoreCase);
        using var reader=cmd.ExecuteReader();
        while(reader.Read())dict.Add(reader.GetString(0),
            (reader.GetString(1),reader.GetString(2)));
        return dict;
    }

    private static string? FindE1HistoricalAsset(SqliteConnection db,
        SqliteTransaction tx,string rootId,string relative,string sha)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT a.id FROM e1_root_result_batches b
            JOIN experiment_assets a ON a.result_id=b.result_id
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            WHERE b.root_id=$root AND a.relative_path=$path
              AND a.sha256=$sha AND a.is_deleted=0
            ORDER BY a.imported_at DESC,a.id DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$root",rootId);
        cmd.Parameters.AddWithValue("$path",relative);
        cmd.Parameters.AddWithValue("$sha",sha);
        return cmd.ExecuteScalar() as string;
    }

    private static void SaveE1Current(SqliteConnection db,SqliteTransaction tx,
        string rootId,string relative,string assetId,string sha)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO e1_root_current_assets(root_id,relative_path,asset_id,sha256)
            VALUES($root,$path,$asset,$sha)
            ON CONFLICT(root_id,relative_path)
            DO UPDATE SET asset_id=excluded.asset_id,sha256=excluded.sha256
            """;
        cmd.Parameters.AddWithValue("$root",rootId);
        cmd.Parameters.AddWithValue("$path",relative);
        cmd.Parameters.AddWithValue("$asset",assetId);
        cmd.Parameters.AddWithValue("$sha",sha);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<E1FolderIdentity> ListE1FolderIdentities(string projectId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT r.id,r.experiment_id,r.project_id,r.experiment_name,r.normalized_root,
                COUNT(CASE WHEN batch.root_id IS NOT NULL
                    AND active.id IS NOT NULL THEN current.asset_id END)
            FROM e1_experiment_roots r
            LEFT JOIN e1_root_current_assets current ON current.root_id=r.id
            LEFT JOIN experiment_assets a ON a.id=current.asset_id AND a.is_deleted=0
            LEFT JOIN e1_root_result_batches batch ON batch.result_id=a.result_id
                AND batch.root_id=r.id
            LEFT JOIN experiment_results active ON active.id=a.result_id
                AND active.is_deleted=0
            WHERE r.project_id=$project
            GROUP BY r.id ORDER BY r.experiment_name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$project",projectId);
        var rows=new List<E1FolderIdentity>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())rows.Add(new(rd.GetString(0),
            rd.IsDBNull(1)?"":rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),rd.GetInt32(5)));
        return rows;
    }

    /// <summary>
    /// Explicitly relink a moved source directory. Every currently tracked
    /// path must still contain the same bytes; no Asset ID or tag is rewritten.
    /// This is never performed automatically from a matching folder name.
    /// </summary>
    public void RelinkE1FolderRoot(string rootId,string newSourceDirectory)
    {
        var path=NormalizeE1Root(newSourceDirectory);
        if(!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);
        var warehouseRoot=NormalizeE1Root(_root);
        if(path.Equals(warehouseRoot,StringComparison.OrdinalIgnoreCase)||
            path.StartsWith(warehouseRoot+"/",StringComparison.OrdinalIgnoreCase))
            throw new IOException("实验根目录不能放在数据仓库内部。");
        var expected=ListE1FolderAssets(rootId);
        if(expected.Count==0)
            throw new InvalidOperationException("文件夹没有可核对的资产，禁止自动改绑目录。");
        foreach(var item in expected)
        {
            var candidate=Path.Combine(path,
                item.RelativePath.Replace('/',Path.DirectorySeparatorChar));
            if(!File.Exists(candidate))
                throw new InvalidOperationException(
                    "新文件夹缺少文件："+item.RelativePath);
            using var stream=File.OpenRead(candidate);
            var actual=Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(stream));
            if(!actual.Equals(item.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "文件内容与原始 SHA-256 不一致："+item.RelativePath);
        }
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var update=db.CreateCommand();
        update.Transaction=tx;
        update.CommandText="""
            UPDATE e1_experiment_roots SET normalized_root=$path WHERE id=$id
            """;
        update.Parameters.AddWithValue("$id",rootId);
        update.Parameters.AddWithValue("$path",path);
        if(update.ExecuteNonQuery()!=1)
            throw new KeyNotFoundException("实验根文件夹索引不存在。");
        tx.Commit();
    }

    public IReadOnlyList<E1FolderAsset> ListE1FolderAssets(string rootId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT current.asset_id,current.relative_path,current.sha256
            FROM e1_root_current_assets current
            JOIN experiment_assets a ON a.id=current.asset_id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN e1_root_result_batches batch ON batch.result_id=a.result_id
                AND batch.root_id=current.root_id
            WHERE current.root_id=$root
            ORDER BY current.relative_path COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$root",rootId);
        var rows=new List<E1FolderAsset>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())rows.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2)));
        return rows;
    }
}
