using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record E6ResearchProject(
    string Id,string Name,string RootPath,int ExperimentCount,int AttachmentCount);
public sealed record E6ResearchExperiment(
    string ResearchId,string ResearchName,string RootId,string ExperimentId,
    string ExperimentName,string RootPath,int ActiveFileCount);
public sealed record E6ResearchAttachment(
    string Id,string RelativePath,string Sha256,long Size);
public sealed record E6ResearchImportPreview(
    string Name,string RootPath,IReadOnlyList<string> ExperimentFolders,
    IReadOnlyList<string> ResearchFiles);
public sealed record E6ResearchImportResult(
    string ResearchId,string Name,int ExperimentCount,int ImportedFiles,
    int ResearchAttachmentCount);

/// <summary>
/// E6 adds an organizational tier above the existing E1 Experiment ID.
/// Paper/Figure/Panel provenance and archived Revision IDs remain untouched.
/// </summary>
public sealed partial class ExperimentWorkspace
{
    private void InitializeE6ResearchProjects()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS e6_research_projects(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                normalized_root TEXT NOT NULL COLLATE NOCASE UNIQUE,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS e6_research_experiments(
                root_id TEXT PRIMARY KEY REFERENCES e1_experiment_roots(id),
                research_id TEXT NOT NULL REFERENCES e6_research_projects(id),
                assigned_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_e6_research_experiments_research
                ON e6_research_experiments(research_id);
            CREATE TABLE IF NOT EXISTS e6_research_attachments(
                id TEXT PRIMARY KEY,
                research_id TEXT NOT NULL REFERENCES e6_research_projects(id),
                relative_path TEXT NOT NULL COLLATE NOCASE,
                sha256 TEXT NOT NULL,
                size INTEGER NOT NULL,
                imported_at TEXT NOT NULL,
                UNIQUE(research_id,relative_path)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static string E6Normalized(string path)=>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\','/');

    private void ValidateE6Source(string root)
    {
        var warehouse=E6Normalized(_root);
        var candidate=E6Normalized(root);
        if(candidate.Equals(warehouse,StringComparison.OrdinalIgnoreCase)||
            candidate.StartsWith(warehouse+"/",StringComparison.OrdinalIgnoreCase)||
            warehouse.StartsWith(candidate+"/",StringComparison.OrdinalIgnoreCase))
            throw new IOException("研究项目文件夹不能与数据仓库相互包含。");
        if(File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("不允许直接导入符号链接目录。");
    }

    public E6ResearchImportPreview PreviewE6ResearchImport(string researchDirectory)
    {
        var root=E6Normalized(researchDirectory);
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        ValidateE6Source(root);
        var children=Directory.EnumerateDirectories(root)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        foreach(var folder in children)
            if(File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("研究项目含有符号链接实验目录："+folder);
        var files=Directory.EnumerateFiles(root, "*",SearchOption.TopDirectoryOnly)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        if(children.Length==0)
            throw new InvalidOperationException(
                "没有找到实验子文件夹。请使用“导入单个实验文件夹”。");
        if(children.All(x=>!Directory.EnumerateFiles(x,"*",SearchOption.AllDirectories).Any()))
            throw new InvalidOperationException("实验子文件夹中没有可导入文件。");
        return new(Path.GetFileName(root),root,
            children.Select(Path.GetFileName).ToArray(),
            files.Select(Path.GetFileName).ToArray());
    }

    public IReadOnlyList<E6ResearchProject> ListE6ResearchProjects()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT p.id,p.name,p.normalized_root,
                (SELECT COUNT(*) FROM e6_research_experiments x WHERE x.research_id=p.id),
                (SELECT COUNT(*) FROM e6_research_attachments a WHERE a.research_id=p.id)
            FROM e6_research_projects p
            ORDER BY p.name COLLATE NOCASE,p.id
            """;
        var result=new List<E6ResearchProject>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetInt32(3),rd.GetInt32(4)));
        return result;
    }

    public IReadOnlyList<E6ResearchExperiment> ListE6ResearchExperiments()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT p.id,p.name,r.id,r.experiment_id,r.experiment_name,
                   r.normalized_root,
                (SELECT COUNT(*) FROM e1_root_current_assets a
                 JOIN experiment_assets asset ON asset.id=a.asset_id
                    AND asset.is_deleted=0
                 JOIN experiment_results result ON result.id=asset.result_id
                    AND result.is_deleted=0
                 JOIN e1_root_result_batches b ON b.result_id=asset.result_id
                    AND b.root_id=r.id
                 WHERE a.root_id=r.id)
            FROM e6_research_experiments x
            JOIN e6_research_projects p ON p.id=x.research_id
            JOIN e1_experiment_roots r ON r.id=x.root_id
            ORDER BY p.name COLLATE NOCASE,r.experiment_name COLLATE NOCASE
            """;
        var rows=new List<E6ResearchExperiment>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())rows.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.IsDBNull(3)?"":rd.GetString(3),
            rd.GetString(4),rd.GetString(5),rd.GetInt32(6)));
        return rows;
    }

    public IReadOnlyList<E6ResearchAttachment> ListE6ResearchAttachments(string researchId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT id,relative_path,sha256,size FROM e6_research_attachments
            WHERE research_id=$id ORDER BY relative_path COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$id",researchId);
        var rows=new List<E6ResearchAttachment>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())rows.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3)==""?0:rd.GetInt64(3)));
        return rows;
    }

    private string EnsureE6ResearchProject(string root,string displayName)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var lookup=db.CreateCommand();
        lookup.Transaction=tx;
        lookup.CommandText="SELECT id FROM e6_research_projects WHERE normalized_root=$root";
        lookup.Parameters.AddWithValue("$root",root);
        var existing=lookup.ExecuteScalar() as string;
        if(existing is not null){tx.Commit();return existing;}
        var id=NewId();
        using var insert=db.CreateCommand();
        insert.Transaction=tx;
        insert.CommandText="""
            INSERT INTO e6_research_projects(id,name,normalized_root,created_at,updated_at)
            VALUES($id,$name,$root,$now,$now)
            """;
        insert.Parameters.AddWithValue("$id",id);
        insert.Parameters.AddWithValue("$name",displayName);
        insert.Parameters.AddWithValue("$root",root);
        insert.Parameters.AddWithValue("$now",UtcNow());
        insert.ExecuteNonQuery();
        tx.Commit();
        return id;
    }

    private E1FolderIdentity? E6FindRootByPath(string root)
    {
        // Use existing physical-root records from ANY old or new project;
        // importing a parent directory must not clone stable experiment IDs.
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT r.id,r.experiment_id,r.project_id,r.experiment_name,
                r.normalized_root FROM e1_experiment_roots r
            WHERE r.normalized_root=$root
            ORDER BY r.created_at,r.id LIMIT 2
            """;
        cmd.Parameters.AddWithValue("$root",root);
        using var rd=cmd.ExecuteReader();
        if(!rd.Read())return null;
        var row=new E1FolderIdentity(rd.GetString(0),rd.IsDBNull(1)?"":rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),0);
        if(rd.Read())throw new InvalidOperationException(
            "该实验目录曾在多个旧项目中登记，请先处理重复身份后再批量导入："+root);
        return row;
    }

    private string GetE6SharedProject()
    {
        var existing=ListProjects().FirstOrDefault(x=>x.Name=="独立实验资料库");
        return existing?.Id??CreateProject("独立实验资料库");
    }

    public E6ResearchImportResult ImportE6ResearchProject(
        string researchDirectory,string? displayName=null)
    {
        var preview=PreviewE6ResearchImport(researchDirectory);
        var name=(displayName??preview.Name).Trim();
        if(name.Length is <1 or >180)throw new ArgumentException("研究项目名称须为 1–180 个字符。");
        var researchId=EnsureE6ResearchProject(preview.RootPath,name);
        var projectId=GetE6SharedProject();
        var imported=0;
        foreach(var folderName in preview.ExperimentFolders)
        {
            var experimentPath=E6Normalized(Path.Combine(preview.RootPath,folderName));
            var existing=E6FindRootByPath(experimentPath);
            // Incremental imports should reuse the existing E1 project, its
            // Experiment ID, and all Panel references. Never clone by name.
            var targetProject=existing?.ProjectId??projectId;
            // Empty child folders are not treated as new experiments.
            if(!Directory.EnumerateFiles(experimentPath,"*",SearchOption.AllDirectories).Any())
                continue;
            var result=ImportResult(targetProject,experimentPath);
            imported+=result.FileCount;
            var root=E6FindRootByPath(experimentPath)
                ??throw new InvalidOperationException("子实验未建立 E1 文件夹索引。");
            if(string.IsNullOrEmpty(root.ExperimentId))
                throw new InvalidOperationException("实验未具有稳定 Experiment ID。");
            AssignE6Experiment(researchId,root.Id);
        }
        foreach(var fileName in preview.ResearchFiles)
            IndexE6ResearchAttachment(researchId,
                Path.Combine(preview.RootPath,fileName),fileName);
        var count=ListE6ResearchExperiments().Count(x=>x.ResearchId==researchId);
        return new(researchId,name,count,imported,
            ListE6ResearchAttachments(researchId).Count);
    }

    public void AssignE6Experiment(string researchId,string rootId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var check=db.CreateCommand();
        check.Transaction=tx;
        check.CommandText="""
            SELECT COUNT(*) FROM e6_research_projects WHERE id=$research
            """;
        check.Parameters.AddWithValue("$research",researchId);
        if(Convert.ToInt64(check.ExecuteScalar())!=1)
            throw new KeyNotFoundException("研究项目不存在。");
        using var update=db.CreateCommand();
        update.Transaction=tx;
        update.CommandText="""
            INSERT INTO e6_research_experiments(root_id,research_id,assigned_at)
            VALUES($root,$research,$now)
            ON CONFLICT(root_id) DO UPDATE SET
               research_id=excluded.research_id,
               assigned_at=excluded.assigned_at
            """;
        update.Parameters.AddWithValue("$root",rootId);
        update.Parameters.AddWithValue("$research",researchId);
        update.Parameters.AddWithValue("$now",UtcNow());
        update.ExecuteNonQuery();
        tx.Commit();
    }

    public void RenameE6ResearchProject(string researchId,string newName)
    {
        var name=newName.Trim();
        if(name.Length is <1 or >180)
            throw new ArgumentException("研究项目名称须为 1–180 个字符。");
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            UPDATE e6_research_projects SET name=$name,updated_at=$now WHERE id=$id
            """;
        cmd.Parameters.AddWithValue("$id",researchId);
        cmd.Parameters.AddWithValue("$name",name);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        if(cmd.ExecuteNonQuery()!=1)throw new KeyNotFoundException("研究项目不存在。");
    }

    private void IndexE6ResearchAttachment(string researchId,string file,string relative)
    {
        if(File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("不允许导入研究项目符号链接附件："+relative);
        var source=new FileInfo(file);
        var staging=Path.Combine(_root,"staging",NewId()+".part");
        string hash;
        try
        {
            using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            using(var stream=File.OpenRead(staging))
                hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var blob=Path.Combine(_root,"blobs","sha256",hash[..2],hash);
            Directory.CreateDirectory(Path.GetDirectoryName(blob)!);
            if(!File.Exists(blob))File.Move(staging,blob);
            else
            {
                using var existing=File.OpenRead(blob);
                if(!Convert.ToHexString(SHA256.HashData(existing))
                    .Equals(hash,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("研究附件仓库内容校验失败。");
            }
        }
        finally{if(File.Exists(staging))File.Delete(staging);}
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            INSERT INTO e6_research_attachments(
                id,research_id,relative_path,sha256,size,imported_at)
            VALUES($id,$research,$path,$sha,$size,$now)
            ON CONFLICT(research_id,relative_path) DO UPDATE SET
                sha256=excluded.sha256,size=excluded.size,imported_at=excluded.imported_at
            """;
        cmd.Parameters.AddWithValue("$id",NewId());
        cmd.Parameters.AddWithValue("$research",researchId);
        cmd.Parameters.AddWithValue("$path",relative);
        cmd.Parameters.AddWithValue("$sha",hash);
        cmd.Parameters.AddWithValue("$size",source.Length);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
    }
}
