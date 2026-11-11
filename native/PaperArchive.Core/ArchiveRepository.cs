using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core;

public sealed record PaperEntry(string Id, string Title, string Status, int FigureCount, int FileCount);
public sealed record ResourceEntry(string DataId, string FileName, string Role, string Group, long Size, string Sha256);
public sealed record ImportPlan(string SourceRelativePath, string LogicalRelativePath, string Role,
    int? Figure, string? Panel, int? Sequence);
public sealed record ImportSummary(string PaperId, int Files, int Figures, int Warnings);

public sealed partial class ArchiveRepository
{
    private readonly string _root;
    private readonly string _connectionString;
    public string Root => _root;

    private ArchiveRepository(string root)
    {
        _root = root;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, "database", "archive.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
    }

    public static ArchiveRepository Open(string root)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(Path.Combine(root, "database"));
        Directory.CreateDirectory(Path.Combine(root, "staging"));
        Directory.CreateDirectory(Path.Combine(root, "blobs", "sha256"));
        var repo = new ArchiveRepository(root);
        repo.Initialize();
        repo.InitializeWorkspaceArchives();
        return repo;
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialize()
    {
        using var db = Connect();
        using (var command = db.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA user_version;";
            command.ExecuteNonQuery();
        }
        var version = ScalarLong(db, "PRAGMA user_version");
        if (version > 3) throw new InvalidOperationException("数据仓库来自更新版本，请勿使用旧客户端打开。");
        if (version == 3) return;

        // Migration is destructive if interrupted; first make an independent copy of the DB.
        var database = Path.Combine(_root, "database", "archive.db");
        if (version > 0 && File.Exists(database))
        {
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_root, "database",
                    "archive-pre-winui-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".db")
            }.ToString());
            backup.Open();
            db.BackupDatabase(backup);
        }
        using var transaction = db.BeginTransaction();
        if (version == 0)
        {
            RunSql(db, transaction, "0001_initial.sql");
            Execute(db, transaction,
                "INSERT INTO archive_meta(singleton,archive_id,schema_version,created_at,updated_at) VALUES(1,$id,1,$time,$time)",
                ("$id", Guid.CreateVersion7().ToString()), ("$time", Now()));
            version = 1;
        }
        if (version == 1)
        {
            RunSql(db, transaction, "0002_paper_workspace.sql");
            version = 2;
        }
        if (version == 2)
        {
            RunSql(db, transaction, "0003_archive_workflow.sql");
            version = 3;
        }
        Execute(db, transaction,
            "UPDATE archive_meta SET schema_version=3,updated_at=$time WHERE singleton=1",
            ("$time", Now()));
        transaction.Commit();
    }

    private static void RunSql(SqliteConnection db, SqliteTransaction transaction, string name)
    {
        var assembly = typeof(ArchiveRepository).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("缺少数据库迁移脚本：" + name);
        using var reader = new StreamReader(stream);
        using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = reader.ReadToEnd();
        cmd.ExecuteNonQuery();
    }

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static long ScalarLong(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Execute(SqliteConnection db, SqliteTransaction transaction,
        string sql, params (string Name, object Value)[] args)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PaperEntry> ListPapers()
    {
        var result = new List<PaperEntry>();
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT p.paper_id,p.title,p.archive_status,
              (SELECT count(*) FROM paper_items i WHERE i.paper_id=p.paper_id AND i.item_type='figure'),
              (SELECT count(*) FROM data_references r JOIN paper_items i ON i.item_id=r.item_id WHERE i.paper_id=p.paper_id)
            FROM papers p ORDER BY p.updated_at DESC
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(new PaperEntry(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4)));
        return result;
    }

    public IReadOnlyList<ResourceEntry> ListResources(string paperId)
    {
        var result = new List<ResourceEntry>();
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT d.data_id,d.original_filename,r.resource_role,i.label,b.file_size,b.sha256,i.sort_order
            FROM data_references r
            JOIN paper_items i ON i.item_id=r.item_id
            JOIN data_records d ON d.data_id=r.data_id
            JOIN blobs b ON b.sha256=d.blob_sha256
            WHERE i.paper_id=$paper AND d.deleted_at IS NULL
            UNION ALL
            SELECT d.data_id,d.original_filename,'other','Others',b.file_size,b.sha256,999999
            FROM paper_other_resources o
            JOIN data_records d ON d.data_id=o.data_id
            JOIN blobs b ON b.sha256=d.blob_sha256
            WHERE o.paper_id=$paper AND d.deleted_at IS NULL
            ORDER BY 7,2
            """;
        cmd.Parameters.AddWithValue("$paper", paperId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(new ResourceEntry(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5)));
        return result;
    }

    public string ResolveOriginal(string sha256)
    {
        if (sha256.Length != 64 || sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("无效的 SHA-256");
        var file = Path.Combine(_root, "blobs", "sha256", sha256[..2], sha256);
        if (!File.Exists(file)) throw new FileNotFoundException("原始文件不在仓库中", file);
        return file;
    }

    public ImportSummary Import(PaperScan scan, IReadOnlyList<ImportPlan> plans)
    {
        var root = Path.GetFullPath(scan.Root);
        if (root.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || _root.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(root, _root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("源文件夹不得包含数据仓库。");
        if (plans.Count != scan.Files.Count) throw new InvalidOperationException("整理计划不完整，请重新扫描。");
        var sources = scan.Files.ToDictionary(f => f.SourceRelativePath, StringComparer.OrdinalIgnoreCase);
        var uniqueSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (!uniqueSources.Add(plan.SourceRelativePath)
                || !sources.ContainsKey(plan.SourceRelativePath)
                || !uniqueNames.Add(plan.LogicalRelativePath)
                || !ValidLogicalPath(plan.LogicalRelativePath))
                throw new InvalidOperationException("整理计划包含重复文件或无效路径。");
            if (plan.Panel is not null && (plan.Panel.Length != 1 || !char.IsAsciiLetter(plan.Panel[0])))
                throw new InvalidOperationException("Panel 编号必须为单个字母。");
            if (plan.Figure is < 1 || plan.Sequence is < 1)
                throw new InvalidOperationException("编号必须为正整数。");
        }
        if (plans.Count(x => x.Role == "manuscript") != 1)
            throw new InvalidOperationException("一篇论文必须指定一份 PDF/DOCX 正文。");

        // Prepare all content-addressed blobs before committing any paper metadata.
        var blobs = new List<(ImportPlan Plan, string Sha, long Size, string Filename, string Extension)>();
        foreach (var plan in plans)
        {
            var source = Path.GetFullPath(Path.Combine(root, plan.SourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("源文件路径越界。");
            var attr = File.GetAttributes(source);
            if (attr.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("禁止导入符号链接。");
            var info = new FileInfo(source);
            if (info.Length != sources[plan.SourceRelativePath].Size)
                throw new IOException("文件在扫描之后发生变化，请重新扫描：" + source);
            var staged = Path.Combine(_root, "staging", Guid.CreateVersion7() + ".part");
            try
            {
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                if (new FileInfo(source).Length != info.Length)
                    throw new IOException("源文件在读取过程中变化。");
                string sha;
                using (var readback = File.OpenRead(staged))
                    sha = Convert.ToHexString(SHA256.HashData(readback)).ToLowerInvariant();
                var destination = Path.Combine(_root, "blobs", "sha256", sha[..2], sha);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Move(staged, destination);
                else
                {
                    using var existing = File.OpenRead(destination);
                    if (!Convert.ToHexString(SHA256.HashData(existing)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("存量 Blob 校验失败：" + sha);
                }
                blobs.Add((plan, sha, info.Length, Path.GetFileName(source),
                    Path.GetExtension(source).TrimStart('.').ToLowerInvariant()));
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }

        var id = Guid.CreateVersion7().ToString();
        var timestamp = Now();
        var groups = new Dictionary<int, string>();
        var panels = new Dictionary<(int, string), string>();
        var tables = new Dictionary<int, string>();
        using var db = Connect();
        using var tx = db.BeginTransaction();
        Execute(db, tx, """
            INSERT INTO papers(paper_id,title,created_at,updated_at,archive_status)
            VALUES($id,$title,$time,$time,'draft')
            """, ("$id", id), ("$title", scan.Title), ("$time", timestamp));
        var count = 0;
        foreach (var (plan, sha, length, filename, extension) in blobs)
        {
            var relative = $"blobs/sha256/{sha[..2]}/{sha}";
            Execute(db, tx, """
                INSERT INTO blobs(sha256,file_size,relative_path,created_at)
                VALUES($sha,$size,$path,$time) ON CONFLICT(sha256) DO NOTHING
                """, ("$sha", sha), ("$size", length), ("$path", relative), ("$time", timestamp));
            var dataId = Guid.CreateVersion7().ToString();
            var category = new[] { "png", "jpg", "jpeg", "tif", "tiff", "webp", "bmp", "czi", "nd2" }.Contains(extension) ? "image"
                : new[] { "xlsx", "xls", "csv", "tsv" }.Contains(extension) ? "spreadsheet"
                : new[] { "pdf", "docx", "doc", "txt" }.Contains(extension) ? "document" : "other";
            Execute(db, tx, """
                INSERT INTO data_records(data_id,display_id,blob_sha256,original_filename,file_extension,category,created_at)
                VALUES($id,$display,$sha,$name,$ext,$cat,$time)
                """, ("$id", dataId), ("$display", "RD-" + dataId.Replace("-", "").ToUpperInvariant()),
                ("$sha", sha), ("$name", filename), ("$ext", extension), ("$cat", category), ("$time", timestamp));
            count++;
            if (plan.Role == "manuscript")
            {
                Execute(db, tx, """
                    UPDATE papers SET manuscript_sha256=$sha,manuscript_filename=$name,manuscript_extension=$ext
                    WHERE paper_id=$paper
                    """, ("$sha", sha), ("$name", filename), ("$ext", extension), ("$paper", id));
                continue;
            }

            string? itemId = null;
            if (plan.Figure is int figure && plan.Role is "composite" or "display" or "stat_image" or "statistics")
            {
                if (plan.Role == "statistics" && plan.Panel is null)
                {
                    if (!tables.TryGetValue(figure, out itemId))
                    {
                        itemId = InsertItem(db, tx, id, null, "table", "Table " + figure, tables.Count, timestamp);
                        tables.Add(figure, itemId);
                    }
                }
                else
                {
                    if (!groups.TryGetValue(figure, out var figureId))
                    {
                        figureId = InsertItem(db, tx, id, null, "figure", "Figure " + figure, groups.Count, timestamp);
                        groups.Add(figure, figureId);
                    }
                    itemId = figureId;
                    if (plan.Panel is { Length: 1 } letter)
                    {
                        var key = (figure, letter.ToUpperInvariant());
                        if (!panels.TryGetValue(key, out itemId))
                        {
                            itemId = InsertItem(db, tx, id, figureId, "panel",
                                "Figure " + figure + letter.ToUpperInvariant(), panels.Count, timestamp);
                            panels.Add(key, itemId);
                        }
                    }
                }
            }
            if (itemId is not null)
            {
                Execute(db, tx, """
                    INSERT INTO data_references(reference_id,data_id,item_id,created_at,resource_role,source_relative_path)
                    VALUES($id,$data,$item,$time,$role,$path)
                    """, ("$id", Guid.CreateVersion7().ToString()), ("$data", dataId),
                    ("$item", itemId), ("$time", timestamp), ("$role", plan.Role), ("$path", plan.LogicalRelativePath));
            }
            else
            {
                Execute(db, tx, """
                    INSERT INTO paper_other_resources(paper_id,data_id,source_relative_path)
                    VALUES($paper,$data,$path)
                    """, ("$paper", id), ("$data", dataId), ("$path", plan.LogicalRelativePath));
            }
        }
        // Immutable relationship snapshot and read-only status, finalized as one SQLite transaction.
        var snapshot = JsonSerializer.Serialize(new { id, scan.Title, files = plans, at = timestamp });
        Execute(db, tx, """
            INSERT INTO paper_archive_snapshots(paper_id,revision,snapshot_json,archived_at)
            VALUES($paper,1,$json,$time)
            """, ("$paper", id), ("$json", snapshot), ("$time", timestamp));
        Execute(db, tx, """
            UPDATE papers SET archive_status='archived',archive_revision=1,archived_at=$time
            WHERE paper_id=$paper
            """, ("$paper", id), ("$time", timestamp));
        tx.Commit();
        return new ImportSummary(id, count, groups.Count, scan.WarningCount);
    }

    private static string InsertItem(SqliteConnection db, SqliteTransaction tx, string paper,
        string? parent, string type, string label, int order, string time)
    {
        var id = Guid.CreateVersion7().ToString();
        Execute(db, tx, """
            INSERT INTO paper_items(item_id,paper_id,parent_id,item_type,label,sort_order,created_at,updated_at)
            VALUES($id,$paper,$parent,$type,$label,$order,$time,$time)
            """, ("$id", id), ("$paper", paper), ("$parent", parent ?? (object)DBNull.Value),
            ("$type", type), ("$label", label), ("$order", order), ("$time", time));
        return id;
    }

    public static bool ValidLogicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)
            || path.Contains('\\') || path.IndexOfAny([':', '|', '<', '>', '"', '?', '*']) >= 0)
            return false;
        return path.Split('/').All(part => !string.IsNullOrWhiteSpace(part)
            && part is not "." and not ".." && !part.EndsWith(' ') && !part.EndsWith('.'));
    }
}
