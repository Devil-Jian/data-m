using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record PaperDocument(
    string Id,string PaperId,string FileName,long Size,string Sha256,string ImportedAt);

public sealed partial class ExperimentWorkspace
{
    private void InitializePaperDocuments()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS paper_documents(
                id TEXT PRIMARY KEY,
                paper_id TEXT NOT NULL REFERENCES paper_projects(id),
                filename TEXT NOT NULL,
                size INTEGER NOT NULL,
                sha256 TEXT NOT NULL,
                imported_at TEXT NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_paper_documents_paper
                ON paper_documents(paper_id,is_deleted);
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PaperDocument> ListPaperDocuments(string paperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT id,paper_id,filename,size,sha256,imported_at
            FROM paper_documents WHERE paper_id=$paper AND is_deleted=0
            ORDER BY imported_at DESC,filename COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$paper",paperId);
        using var rd=cmd.ExecuteReader();
        var list=new List<PaperDocument>();
        while(rd.Read())list.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetInt64(3),rd.GetString(4),rd.GetString(5)));
        return list;
    }

    public PaperDocument ImportPaperDocument(string paperId,string sourceFile)
    {
        var source=Path.GetFullPath(sourceFile);
        var extension=Path.GetExtension(source).ToLowerInvariant();
        if(extension is not (".pdf" or ".doc" or ".docx"))
            throw new ArgumentException("论文全文仅支持 PDF、DOC 和 DOCX。");
        if(!File.Exists(source))throw new FileNotFoundException("论文文档不存在。",source);
        using(var db=Connect())
        using(var check=db.CreateCommand())
        {
            check.CommandText="SELECT COUNT(*) FROM paper_projects WHERE id=$id";
            check.Parameters.AddWithValue("$id",paperId);
            if(Convert.ToInt64(check.ExecuteScalar())!=1)
                throw new KeyNotFoundException("论文项目不存在。");
        }

        var staging=Path.Combine(_root,"staging",NewId()+".part");
        try
        {
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
            var size=new FileInfo(staging).Length;
            string sha;
            using(var stream=File.OpenRead(staging))
                sha=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var path=Path.Combine(_root,"blobs","sha256",sha[..2],sha);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if(!File.Exists(path))File.Move(staging,path);
            else
            {
                using var stream=File.OpenRead(path);
                if(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()!=sha)
                    throw new IOException("目标仓库中同名哈希文件校验失败。");
            }
            var doc=new PaperDocument(NewId(),paperId,Path.GetFileName(source),size,sha,UtcNow());
            using var db=Connect();
            using var cmd=db.CreateCommand();
            cmd.CommandText="""
                INSERT INTO paper_documents(id,paper_id,filename,size,sha256,imported_at)
                VALUES($id,$paper,$name,$size,$sha,$now)
                """;
            cmd.Parameters.AddWithValue("$id",doc.Id);
            cmd.Parameters.AddWithValue("$paper",paperId);
            cmd.Parameters.AddWithValue("$name",doc.FileName);
            cmd.Parameters.AddWithValue("$size",doc.Size);
            cmd.Parameters.AddWithValue("$sha",doc.Sha256);
            cmd.Parameters.AddWithValue("$now",doc.ImportedAt);
            cmd.ExecuteNonQuery();
            return doc;
        }
        finally{if(File.Exists(staging))File.Delete(staging);}
    }

    public string GetPaperDocumentPath(PaperDocument document)
    {
        if(document.Sha256.Length!=64||
           document.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new InvalidDataException("不合法的论文文档哈希。");
        var path=Path.Combine(_root,"blobs","sha256",document.Sha256[..2],document.Sha256);
        if(!File.Exists(path))throw new FileNotFoundException("归档论文文件不存在。",path);
        return path;
    }
}
