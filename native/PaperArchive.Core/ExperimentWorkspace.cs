using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record ExperimentProject(string Id, string Name);
public sealed record ExperimentResult(string Id, string ProjectId, string Name, int FileCount);
public sealed record ExperimentAsset(
    string Id, string ResultId, string GroupId, string GroupPrefix, string Stage,
    string RelativePath, string FileName, long Size, string Sha256);
public sealed record PaperUsage(
    string Id, string AssetId, string PaperTitle, int Figure, string Panel, string State, string ExportPath);
public sealed record WorkspaceImportResult(string ResultId, int FileCount, int DataGroupCount);

/// <summary>
/// Separate experimental workspace. Legacy paper archive database and migrations are untouched.
/// Imported content is copied byte-for-byte into the existing content-addressed blob store.
/// </summary>
public sealed partial class ExperimentWorkspace
{
    private readonly string _root;
    private readonly string _connectionString;
    public string Root => _root;

    private ExperimentWorkspace(string root)
    {
        _root = Path.GetFullPath(root);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "database", "workspace.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public static ExperimentWorkspace Open(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "database"));
        Directory.CreateDirectory(Path.Combine(root, "staging"));
        Directory.CreateDirectory(Path.Combine(root, "blobs", "sha256"));
        var workspace = new ExperimentWorkspace(root);
        workspace.Initialize();
        workspace.InitializeV13();
        workspace.InitializeQuantImageStage();
        workspace.InitializeV13Tree();
        workspace.InitializeUndo();
        workspace.InitializePaperDocuments();
        workspace.InitializeFigureWorkspace();
        workspace.InitializeFigureDropUndo();
        workspace.InitializeProvenance();
        workspace.InitializePaperWorkPreferences();
        workspace.InitializeSampleIndex();
        workspace.InitializeSystemTags();
        return workspace;
    }

    private SqliteConnection Connect()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return db;
    }

    private void Initialize()
    {
        using var db = Connect();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS experiment_projects (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS experiment_results (
                id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES experiment_projects(id),
                name TEXT NOT NULL, imported_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS experiment_groups (
                id TEXT PRIMARY KEY, result_id TEXT NOT NULL REFERENCES experiment_results(id),
                prefix TEXT NOT NULL COLLATE NOCASE,
                UNIQUE(result_id, prefix)
            );
            CREATE TABLE IF NOT EXISTS experiment_assets (
                id TEXT PRIMARY KEY, result_id TEXT NOT NULL REFERENCES experiment_results(id),
                group_id TEXT NOT NULL REFERENCES experiment_groups(id),
                stage TEXT NOT NULL CHECK(stage IN ('raw','process','show','statistics','other')),
                relative_path TEXT NOT NULL, filename TEXT NOT NULL,
                size INTEGER NOT NULL, sha256 TEXT NOT NULL, imported_at TEXT NOT NULL,
                UNIQUE(result_id, relative_path)
            );
            CREATE INDEX IF NOT EXISTS ix_experiment_assets_group ON experiment_assets(group_id);
            CREATE TABLE IF NOT EXISTS experiment_usages (
                id TEXT PRIMARY KEY, asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                paper_title TEXT NOT NULL, figure INTEGER NOT NULL CHECK(figure > 0),
                panel TEXT NOT NULL, state TEXT NOT NULL CHECK(state IN ('exported','confirmed','cancelled')),
                export_path TEXT NOT NULL, created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static string NewId() => Guid.CreateVersion7().ToString();
    private static string UtcNow() => DateTime.UtcNow.ToString("O");

    public IReadOnlyList<ExperimentProject> ListProjects()
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,name FROM experiment_projects WHERE is_deleted=0 ORDER BY name COLLATE NOCASE";
        using var reader = cmd.ExecuteReader();
        var list = new List<ExperimentProject>();
        while (reader.Read()) list.Add(new(reader.GetString(0), reader.GetString(1)));
        return list;
    }

    public string CreateProject(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 180) throw new ArgumentException("实验项目名称不能为空或超过 180 字符。");
        var id = NewId();
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO experiment_projects(id,name,created_at) VALUES($id,$name,$now)";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$now", UtcNow());
        cmd.ExecuteNonQuery();
        return id;
    }

    public IReadOnlyList<ExperimentResult> ListResults(string projectId)
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT r.id,r.project_id,r.name,COUNT(CASE WHEN a.is_deleted=0 THEN a.id END)
            FROM experiment_results r LEFT JOIN experiment_assets a ON a.result_id=r.id
            WHERE r.project_id=$id AND r.is_deleted=0 GROUP BY r.id ORDER BY r.imported_at DESC
            """;
        cmd.Parameters.AddWithValue("$id", projectId);
        using var reader = cmd.ExecuteReader();
        var list = new List<ExperimentResult>();
        while (reader.Read()) list.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return list;
    }

    public IReadOnlyList<ExperimentAsset> ListAssets(string resultId)
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,a.relative_path,a.filename,a.size,a.sha256
            FROM experiment_assets a JOIN experiment_groups g ON g.id=a.group_id
            WHERE a.result_id=$id AND a.is_deleted=0
            ORDER BY a.relative_path COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$id", resultId);
        using var reader = cmd.ExecuteReader();
        var list = new List<ExperimentAsset>();
        while (reader.Read()) list.Add(new(
            reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
            reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetInt64(7),reader.GetString(8)));
        return list;
    }

    public IReadOnlyList<PaperUsage> ListUsages(string assetId)
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT id,asset_id,paper_title,figure,panel,state,export_path
            FROM experiment_usages WHERE asset_id=$id ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("$id", assetId);
        using var reader = cmd.ExecuteReader();
        var list = new List<PaperUsage>();
        while (reader.Read()) list.Add(new(
            reader.GetString(0),reader.GetString(1),reader.GetString(2),
            reader.GetInt32(3),reader.GetString(4),reader.GetString(5),reader.GetString(6)));
        return list;
    }

    public string GetBlobPath(ExperimentAsset asset)
    {
        if (!Regex.IsMatch(asset.Sha256, "^[a-f0-9]{64}$"))
            throw new InvalidDataException("无效的 SHA-256。");
        var path = Path.Combine(_root, "blobs", "sha256", asset.Sha256[..2], asset.Sha256);
        if (!File.Exists(path)) throw new FileNotFoundException("仓库文件不存在。", path);
        return path;
    }

    // A folder name decides its stage; the filename prefix decides its data group.
    // This establishes SAME-GROUP membership, NOT an unverified processing sequence.
    public static string DetectStage(string relativePath)
    {
        var ext = Path.GetExtension(relativePath);
        // Explicit fixed stage labels also work when files are at the root,
        // while the old stage-folder fallback remains backward compatible.
        if(TagImageExtensions.Contains(ext))
        {
            var stem=Path.GetFileNameWithoutExtension(relativePath);
            var version=TagVersionSuffix.Match(stem);
            if(version.Success)stem=stem[..version.Index];
            var fixedTag=TagFixedSuffix.Match(stem);
            if(fixedTag.Success)return fixedTag.Groups[2].Value.ToLowerInvariant();
        }
        // The dedicated suffix takes precedence over generic folders such
        // as process_data; these images are for quantitative analysis only.
        if (QuantImageSuffix.IsMatch(Path.GetFileNameWithoutExtension(relativePath)) &&
            QuantImageExtensions.Contains(ext,StringComparer.OrdinalIgnoreCase))
            return "quant";
        var parts = relativePath.Replace('\\','/').Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("raw_data", StringComparison.OrdinalIgnoreCase)) return "raw";
            if (parts[i].Equals("process_data", StringComparison.OrdinalIgnoreCase)) return "process";
            if (parts[i].Equals("show_data", StringComparison.OrdinalIgnoreCase)) return "show";
            if (parts[i].Equals("quant_data", StringComparison.OrdinalIgnoreCase))
            {
                if (QuantImageExtensions.Contains(ext,StringComparer.OrdinalIgnoreCase))
                    return "quant";
                // Only images are QUANT. CSV/XLSX remain statistics tables.
                break;
            }
        }
        return ext.Equals(".csv",StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xlsx",StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xls",StringComparison.OrdinalIgnoreCase) ? "statistics" : "other";
    }

    private static readonly string[] QuantImageExtensions =
        [".png",".tif",".tiff",".jpg",".jpeg",".bmp",".gif",".webp"];
    private static readonly Regex QuantImageSuffix = new(
        @"(?i)[_-]quant(?:[_-]v\d+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProcessingSuffix = new(
        @"(?i)(?:[_-](?:raw|original|show|display|quant|processed?|crop|cropped|adjusted|adjust|final|merge|merged|enhanced|normalized|filter|filtered|v[0-9]+))(?:[_-]v[0-9]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string DetectPrefix(string relativePath)
    {
        var name = Path.GetFileNameWithoutExtension(relativePath).Trim();
        if (string.IsNullOrEmpty(name)) return "未命名";
        var prefix = ProcessingSuffix.Replace(name, "").TrimEnd('_','-',' ');
        return string.IsNullOrWhiteSpace(prefix) ? name : prefix;
    }

    public WorkspaceImportResult ImportResult(string projectId, string sourceDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (source.Equals(_root, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(_root + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            || _root.StartsWith(source + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("源文件夹不能与数据仓库相互包含。");
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("目录中没有文件。");
        var resultId = NewId();
        var prepared = new List<(string Relative,string FileName,string Stage,string Prefix,long Size,string Sha)>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(source,file).Replace('\\','/');
            var parts = Path.GetRelativePath(source,file).Split(Path.DirectorySeparatorChar);
            var cursor = source;
            foreach (var part in parts)
            {
                cursor=Path.Combine(cursor,part);
                if (File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("不允许导入符号链接：" + relative);
            }
            var staged = Path.Combine(_root,"staging",NewId()+".part");
            try
            {
                var before = new FileInfo(file);
                using (var input = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                using (var output = new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                if (new FileInfo(file).Length != before.Length)
                    throw new IOException("源文件复制期间发生变化：" + relative);
                string sha;
                using (var data = File.OpenRead(staged))
                    sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                var target=Path.Combine(_root,"blobs","sha256",sha[..2],sha);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target)) File.Move(staged,target);
                else
                {
                    using var existing=File.OpenRead(target);
                    if (!Convert.ToHexString(SHA256.HashData(existing)).Equals(sha,StringComparison.OrdinalIgnoreCase))
                        throw new IOException("仓库中已有文件校验失败：" + target);
                }
                prepared.Add((relative,Path.GetFileName(file),DetectStage(relative),DetectPrefix(relative),before.Length,sha));
            }
            finally { if(File.Exists(staged))File.Delete(staged); }
        }
        // The non-RAW/SHOW processing suffix is intentionally open-ended.
        // Use same-import sample anchors to group CV01_anyOperation with CV01
        // without inventing an actual processing lineage or operation order.
        var sampleAnchors=prepared
            .Where(x=>x.Stage is "raw" or "show" or "quant")
            .Select(x=>x.Prefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x=>x.Length)
            .ToArray();
        for(var i=0;i<prepared.Count;i++)
        {
            var item=prepared[i];
            var isUnknownImage=item.Stage=="other" &&
                TagImageExtensions.Contains(Path.GetExtension(item.FileName));
            if(!(item.Stage=="process"||isUnknownImage) ||
                (item.Stage=="process" &&
                    sampleAnchors.Contains(item.Prefix,StringComparer.OrdinalIgnoreCase)))
                continue;
            var stem=Path.GetFileNameWithoutExtension(item.FileName);
            var candidates=sampleAnchors.Where(prefix=>
                stem.StartsWith(prefix+"_",StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith(prefix+"-",StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x=>x.Length).ToArray();
            if(candidates.Length==0 ||
                candidates.Length>1 && candidates[0].Length==candidates[1].Length)
                continue;
            prepared[i]=(item.Relative,item.FileName,
                isUnknownImage?"process":item.Stage,candidates[0],item.Size,item.Sha);
        }
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using (var insert=db.CreateCommand())
        {
            insert.Transaction=tx;
            insert.CommandText="INSERT INTO experiment_results(id,project_id,name,imported_at) VALUES($id,$project,$name,$now)";
            insert.Parameters.AddWithValue("$id",resultId);
            insert.Parameters.AddWithValue("$project",projectId);
            insert.Parameters.AddWithValue("$name",Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
            insert.Parameters.AddWithValue("$now",UtcNow());
            insert.ExecuteNonQuery();
        }
        // Preserve the selected import-root label, independent of result and project names.
        using(var label=db.CreateCommand())
        {
            label.Transaction=tx;
            label.CommandText="""
                INSERT INTO experiment_result_labels(result_id,experiment_name,source_kind)
                VALUES($result,$name,'root_folder')
                """;
            label.Parameters.AddWithValue("$result",resultId);
            label.Parameters.AddWithValue("$name",
                Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar)));
            label.ExecuteNonQuery();
        }
        var groups=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in prepared)
        {
            if(!groups.TryGetValue(item.Prefix,out var groupId))
            {
                groupId=NewId();
                groups.Add(item.Prefix,groupId);
                using var g=db.CreateCommand();
                g.Transaction=tx;
                g.CommandText="INSERT INTO experiment_groups(id,result_id,prefix) VALUES($id,$result,$prefix)";
                g.Parameters.AddWithValue("$id",groupId);
                g.Parameters.AddWithValue("$result",resultId);
                g.Parameters.AddWithValue("$prefix",item.Prefix);
                g.ExecuteNonQuery();
            }
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="""
                INSERT INTO experiment_assets(id,result_id,group_id,stage,relative_path,filename,size,sha256,imported_at)
                VALUES($id,$result,$group,$stage,$path,$name,$size,$sha,$now)
                """;
            cmd.Parameters.AddWithValue("$id",NewId());
            cmd.Parameters.AddWithValue("$result",resultId);
            cmd.Parameters.AddWithValue("$group",groupId);
            cmd.Parameters.AddWithValue("$stage",item.Stage);
            cmd.Parameters.AddWithValue("$path",item.Relative);
            cmd.Parameters.AddWithValue("$name",item.FileName);
            cmd.Parameters.AddWithValue("$size",item.Size);
            cmd.Parameters.AddWithValue("$sha",item.Sha);
            cmd.Parameters.AddWithValue("$now",UtcNow());
            cmd.ExecuteNonQuery();
        }
        // Keep cross-batch sample hints current without altering legacy group IDs.
        RefreshSampleIndex(db,tx,projectId);
        tx.Commit();
        return new(resultId,prepared.Count,groups.Count);
    }

    public string ExportOriginal(ExperimentAsset asset,string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var stem=Path.GetFileNameWithoutExtension(asset.FileName);
        var ext=Path.GetExtension(asset.FileName);
        var destination=Path.Combine(destinationDirectory,asset.FileName);
        for(int i=2;File.Exists(destination);i++)
        {
            if (i>10000) throw new IOException("目标目录中存在过多重名文件。");
            destination=Path.Combine(destinationDirectory,$"{stem} ({i}){ext}");
        }
        using(var source=File.OpenRead(GetBlobPath(asset)))
        using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            source.CopyTo(output);
        using var check=File.OpenRead(destination);
        if (!Convert.ToHexString(SHA256.HashData(check)).Equals(asset.Sha256,StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new IOException("导出校验失败。");
        }
        return destination;
    }

    public PaperUsage ExportForFigure(ExperimentAsset asset,string paperTitle,int figure,string panel,string directory)
    {
        if(asset.Stage!="show")throw new InvalidOperationException("正式组图导出应选择 show_data 图片。");
        if(string.IsNullOrWhiteSpace(paperTitle)||figure<1||!Regex.IsMatch(panel,@"^[A-Za-z]$"))
            throw new ArgumentException("论文、Figure 和 Panel 必须有效。");
        var exported=ExportOriginal(asset,directory);
        var usage=new PaperUsage(NewId(),asset.Id,paperTitle.Trim(),figure,panel.ToUpperInvariant(),"exported",exported);
        try
        {
            using var db=Connect();
            using var cmd=db.CreateCommand();
            cmd.CommandText="""
                INSERT INTO experiment_usages(id,asset_id,paper_title,figure,panel,state,export_path,created_at)
                VALUES($id,$asset,$paper,$figure,$panel,$state,$path,$now)
                """;
            cmd.Parameters.AddWithValue("$id",usage.Id);
            cmd.Parameters.AddWithValue("$asset",usage.AssetId);
            cmd.Parameters.AddWithValue("$paper",usage.PaperTitle);
            cmd.Parameters.AddWithValue("$figure",usage.Figure);
            cmd.Parameters.AddWithValue("$panel",usage.Panel);
            cmd.Parameters.AddWithValue("$state",usage.State);
            cmd.Parameters.AddWithValue("$path",usage.ExportPath);
            cmd.Parameters.AddWithValue("$now",UtcNow());
            cmd.ExecuteNonQuery();
            return usage;
        }
        catch
        {
            if (File.Exists(exported)) File.Delete(exported);
            throw;
        }
    }

    public void SetUsageState(string usageId,string state)
    {
        if(state is not ("exported" or "confirmed" or "cancelled"))throw new ArgumentException("无效的使用状态。");
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE experiment_usages SET state=$state WHERE id=$id";
        cmd.Parameters.AddWithValue("$state",state);
        cmd.Parameters.AddWithValue("$id",usageId);
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("使用记录不存在。");
    }
}
