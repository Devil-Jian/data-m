using System.Text.Json;
using Microsoft.Data.Sqlite;
using PaperArchive.Core.Workspace;

static void Usage()=>Console.Error.WriteLine("""
    V1.6-E2 Panel↔Experiment 关联维护工具
    preview <warehouse>
    apply <warehouse> <fingerprint> <externalBackupDirectory>
    undo-migration <warehouse> <sessionId>
    link <warehouse> <panelId> <experimentId>
    replace <warehouse> <panelId> <experimentId>
    unlink <warehouse> <panelId>
    undo <warehouse> <changeId>
    show <warehouse> <panelId>
    files <warehouse> <panelId>
    """);
static void Output(object? value)=>Console.WriteLine(JsonSerializer.Serialize(
    value,new JsonSerializerOptions{WriteIndented=true}));

static string BackupBeforeSchemaInitialization(string root,string directory)
{
    var full=Path.GetFullPath(root);
    var backup=Path.GetFullPath(directory);
    if(backup.Equals(full,StringComparison.OrdinalIgnoreCase)||
        backup.StartsWith(full+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("SQLite 备份必须保存在数据仓库之外。");
    var sourcePath=Path.Combine(full,"database","workspace.db");
    if(!File.Exists(sourcePath))
        throw new FileNotFoundException("E2 要求已有的 workspace.db。",sourcePath);
    Directory.CreateDirectory(backup);
    var output=Path.Combine(backup,"e2-pre-schema-"+
        DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+
        Guid.NewGuid().ToString("N")+".db");
    using var source=new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource=sourcePath,Mode=SqliteOpenMode.ReadOnly,Pooling=false
    }.ToString());
    using var dest=new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource=output,Mode=SqliteOpenMode.ReadWriteCreate,Pooling=false
    }.ToString());
    source.Open();
    dest.Open();
    source.BackupDatabase(dest);
    return output;
}
if(args.Length<2){Usage();return 2;}
try
{
    var command=args[0].ToLowerInvariant();
    var root=args[1];
    if(command=="preview"&&args.Length==2)
    {
        // A read-only preview does not run schema upgrades. The E2 schema
        // must already have been initialized by the newer client.
        Output(ExperimentWorkspace.OpenForE0Audit(root).PreviewE2LegacyMigration());
        return 0;
    }
    if(command=="apply"&&args.Length==4)
    {
        var preSchema=BackupBeforeSchemaInitialization(root,args[3]);
        var applied=ExperimentWorkspace.Open(root)
            .ApplyE2LegacyMigration(args[2],args[3]);
        Output(new{applied.SessionId,applied.LinkedCount,applied.SkippedCount,
            applied.BackupPath,PreSchemaBackup=preSchema});
        return 0;
    }
    var workspace=ExperimentWorkspace.Open(root);
    if(command=="undo-migration"&&args.Length==3)
    {
        Output(new{Undone=workspace.UndoE2LegacyMigration(args[2])});
        return 0;
    }
    if(command=="link"&&args.Length==4)
    {
        Output(workspace.LinkPanelExperiment(args[2],args[3]));
        return 0;
    }
    if(command=="replace"&&args.Length==4)
    {
        Output(workspace.LinkPanelExperiment(args[2],args[3],replace:true));
        return 0;
    }
    if(command=="unlink"&&args.Length==3)
    {
        Output(workspace.UnlinkPanelExperiment(args[2]));
        return 0;
    }
    if(command=="undo"&&args.Length==3)
    {
        Output(new{Undone=workspace.UndoPanelExperimentChange(args[2])});
        return 0;
    }
    if(command=="show"&&args.Length==3)
    {
        Output(workspace.GetPanelExperiment(args[2]));
        return 0;
    }
    if(command=="files"&&args.Length==3)
    {
        Output(workspace.ListPanelExperimentFiles(args[2]));
        return 0;
    }
    Usage();
    return 2;
}
catch(Exception ex)
{
    Console.Error.WriteLine("E2 操作未完成："+ex.Message);
    return 1;
}
