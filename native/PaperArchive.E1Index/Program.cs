using System.Text.Json;
using Microsoft.Data.Sqlite;
using PaperArchive.Core.Workspace;

static void Usage()
{
    Console.Error.WriteLine("""
        E1 实验文件夹索引工具（开发/迁移模式）
        projects <warehouse>
        folders <warehouse> <projectId>
        preview <warehouse> <projectId>
        apply <warehouse> <projectId> <previewFingerprint> <backupDirectory>
        undo <warehouse> <migrationSessionId>
        relink <warehouse> <folderRootId> <newFolderPath>
        report <warehouse> [projectId]
        """);
}
static string MakeEarlyBackup(string root,string directory)
{
    var full=Path.GetFullPath(root);
    var backup=Path.GetFullPath(directory);
    if(backup.Equals(full,StringComparison.OrdinalIgnoreCase)||
        backup.StartsWith(full+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("备份目录不能位于仓库内。");
    Directory.CreateDirectory(backup);
    var sourcePath=Path.Combine(full,"database","workspace.db");
    if(!File.Exists(sourcePath))
        throw new FileNotFoundException("缺少 workspace.db",sourcePath);
    var output=Path.Combine(backup,"e1-pre-schema-"+DateTime.UtcNow
        .ToString("yyyyMMddHHmmssfff")+".db");
    using var source=new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource=sourcePath,Mode=SqliteOpenMode.ReadOnly,
        Pooling=false
    }.ToString());
    using var destination=new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource=output,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false
    }.ToString());
    source.Open();
    destination.Open();
    source.BackupDatabase(destination);
    return output;
}
static void Output(object value)=>Console.WriteLine(
    JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));

if(args.Length<2){Usage();return 2;}
try
{
    var command=args[0].ToLowerInvariant();
    var root=args[1];
    if(command=="projects"&&args.Length==2)
    {
        Output(ExperimentWorkspace.OpenForE0Audit(root).ListProjects());
        return 0;
    }
    if(command=="preview"&&args.Length==3)
    {
        Output(ExperimentWorkspace.OpenForE0Audit(root)
            .PreviewE1LegacyMigration(args[2]));
        return 0;
    }
    if(command=="report"&&args.Length is 2 or 3)
    {
        var audit=ExperimentWorkspace.OpenForE0Audit(root);
        Output(args.Length==2?audit.PreviewE0Migration():
            audit.PreviewE1LegacyMigration(args[2]));
        return 0;
    }
    if(command=="folders"&&args.Length==3)
    {
        Output(ExperimentWorkspace.OpenForE0Audit(root)
            .ListE1FolderIdentities(args[2]));
        return 0;
    }
    if(command=="apply"&&args.Length==5)
    {
        var preSchema=MakeEarlyBackup(root,args[4]);
        var workspace=ExperimentWorkspace.Open(root);
        var result=workspace.ApplyE1LegacyMigration(args[2],args[3],args[4]);
        Output(new {result.SessionId,result.TagsAdded,
            result.BackupPath,PreSchemaBackup=preSchema});
        return 0;
    }
    if(command=="undo"&&args.Length==3)
    {
        Output(new{Undone=ExperimentWorkspace.Open(root)
            .UndoE1LegacyMigration(args[2])});
        return 0;
    }
    if(command=="relink"&&args.Length==4)
    {
        ExperimentWorkspace.Open(root).RelinkE1FolderRoot(args[2],args[3]);
        Output(new {Relinked=true,RootId=args[2]});
        return 0;
    }
    Usage();return 2;
}
catch(Exception ex)
{
    Console.Error.WriteLine("E1 操作失败："+ex.Message);
    return 1;
}
