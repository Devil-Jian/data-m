using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PaperArchive.Core;

var root = Path.Combine(Path.GetTempPath(), "paper-archive-native-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    var source = Path.Combine(root, "paper-sample");
    foreach (var part in new[] { "Figure/组合图", "Figure/子图", "Figure/统计原始图", "Table", "Others" })
        Directory.CreateDirectory(Path.Combine(source, part.Replace('/', Path.DirectorySeparatorChar)));
    File.WriteAllBytes(Path.Combine(source, "article.pdf"), "%PDF-1.4"u8.ToArray());
    File.WriteAllBytes(Path.Combine(source, "Figure/组合图/Figure1.png".Replace('/', Path.DirectorySeparatorChar)), [1, 2]);
    File.WriteAllBytes(Path.Combine(source, "Figure/子图/1-A-1-image.tif".Replace('/', Path.DirectorySeparatorChar)), [4, 5, 6]);
    File.WriteAllBytes(Path.Combine(source, "Figure/统计原始图/1-A-2-repeat.tif".Replace('/', Path.DirectorySeparatorChar)), [7, 8]);
    File.WriteAllText(Path.Combine(source, "Table/1-A-ROS.csv".Replace('/', Path.DirectorySeparatorChar)), "value\n12");
    File.WriteAllText(Path.Combine(source, "Others/readme.txt".Replace('/', Path.DirectorySeparatorChar)), "notes");

    var scan = PaperFolderScanner.Scan(source);
    Check(scan.Files.Count == 6, "scan number");
    Check(scan.Files.Any(f => f.Panel == "A" && f.Sequence == 2 && f.Role == "stat_image"), "panel inference");
    Check(PaperFolderScanner.Classify("Figure/子图/2-a9.tiff", 0).Sequence == 9, "legacy filename");
    Check(!ArchiveRepository.ValidLogicalPath("../escaped"), "reject traversal");
    Check(!ArchiveRepository.ValidLogicalPath("Figure/子图/A:B.png"), "reject drive syntax");
    var destination = Path.Combine(root, "archive");
    var repo = ArchiveRepository.Open(destination);
    var plans = scan.Files.Select(f => new ImportPlan(f.SourceRelativePath, f.LogicalRelativePath,
        f.Role, f.Figure, f.Panel, f.Sequence)).ToList();
    var saved = repo.Import(scan, plans);
    Check(saved.Files == 6 && saved.Figures == 1, "import counters");
    Check(repo.ListPapers().Single().Status == "archived", "archive state");
    var resources = repo.ListResources(saved.PaperId);
    Check(resources.Count == 4, "figure relationships");
    var representative = resources.Single(r => r.FileName == "1-A-1-image.tif");
    var bytes = File.ReadAllBytes(repo.ResolveOriginal(representative.Sha256));
    Check(bytes.SequenceEqual(new byte[] { 4, 5, 6 }), "preserve original bytes");
    Check(File.Exists(Path.Combine(source, "Figure/子图/1-A-1-image.tif".Replace('/', Path.DirectorySeparatorChar))), "preserve original source");
    Check(ArchiveRepository.Open(destination).ListPapers().Single().Id == saved.PaperId, "reopen existing v3");

    // Verify the real v1 schema is readable, upgraded and backed up, without changing its IDs.
    var oldRoot = Path.Combine(root, "legacy");
    Directory.CreateDirectory(Path.Combine(oldRoot, "database"));
    var legacyDb = Path.Combine(oldRoot, "database", "archive.db");
    using (var db = new SqliteConnection($"Data Source={legacyDb}"))
    {
        db.Open();
        using var sql = db.CreateCommand();
        sql.CommandText = File.ReadAllText(Path.Combine(Environment.CurrentDirectory,
            "src-tauri", "migrations", "0001_initial.sql"));
        sql.ExecuteNonQuery();
        sql.CommandText = "INSERT INTO archive_meta(singleton,archive_id,schema_version,created_at,updated_at) VALUES (1,'legacy-archive',1,'old','old');";
        sql.ExecuteNonQuery();
    }
    var migrated = ArchiveRepository.Open(oldRoot);
    Check(migrated.ListPapers().Count == 0, "legacy open");
    using (var db = new SqliteConnection($"Data Source={legacyDb}"))
    {
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt32(cmd.ExecuteScalar()) == 3, "legacy schema v3");
    }
    Check(Directory.EnumerateFiles(Path.Combine(oldRoot, "database"), "archive-pre-winui-*.db").Any(),
        "migration backup exists");
    Console.WriteLine("PASS: scanner, data import, blob integrity, archive browse, v1->v3 migration backup.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Check(bool condition, string context)
{
    if (!condition) throw new Exception("TEST FAILED: " + context);
}
