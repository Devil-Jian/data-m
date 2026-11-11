using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PaperArchive.Core.Workspace;

namespace PaperArchive.Core;

public sealed record ArchiveCheck(string Code,string Level,string Message);
public sealed record ArchiveResourceSnapshot(string SourceId,string FileName,string Role,
    string Location,string Sha256,long Size);
public sealed record WorkspaceArchivePreview(string PaperId,string Title,string DocumentId,
    IReadOnlyList<ArchiveResourceSnapshot> Resources,IReadOnlyList<ArchiveCheck> Checks,
    string ManifestJson,string ManifestSha256,int FigureCount)
{
    public bool CanPublish=>Checks.All(x=>x.Level!="block");
}
public sealed record WorkspaceArchiveResult(string PaperId,int Revision,bool AlreadyArchived,
    string ManifestSha256,int FileCount);
public sealed record WorkspaceArchiveRevision(int Revision,string ManifestSha256,string ArchivedAt);

public sealed partial class ArchiveRepository
{
    private void InitializeWorkspaceArchives()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS workspace_archive_papers(
              workspace_paper_id TEXT PRIMARY KEY,
              archive_paper_id TEXT NOT NULL UNIQUE REFERENCES papers(paper_id)
            );
            CREATE TABLE IF NOT EXISTS workspace_archive_revisions(
              archive_paper_id TEXT NOT NULL REFERENCES papers(paper_id),
              revision INTEGER NOT NULL,
              document_id TEXT NOT NULL,
              manifest_sha256 TEXT NOT NULL,
              manifest_json TEXT NOT NULL,
              archived_at TEXT NOT NULL,
              PRIMARY KEY(archive_paper_id,revision),
              UNIQUE(archive_paper_id,manifest_sha256)
            );
            CREATE TABLE IF NOT EXISTS workspace_archive_resources(
              archive_paper_id TEXT NOT NULL,
              revision INTEGER NOT NULL,
              source_id TEXT NOT NULL,
              filename TEXT NOT NULL,
              role TEXT NOT NULL,
              location TEXT NOT NULL,
              sha256 TEXT NOT NULL,
              size INTEGER NOT NULL,
              PRIMARY KEY(archive_paper_id,revision,source_id,role,location),
              FOREIGN KEY(archive_paper_id,revision)
                REFERENCES workspace_archive_revisions(archive_paper_id,revision)
            );
            CREATE INDEX IF NOT EXISTS ix_workspace_archive_resources_paper
              ON workspace_archive_resources(archive_paper_id,revision);
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<WorkspaceArchiveRevision> ListWorkspaceRevisions(string archivePaperId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT revision,manifest_sha256,archived_at
            FROM workspace_archive_revisions WHERE archive_paper_id=$paper
            ORDER BY revision DESC
            """;
        cmd.Parameters.AddWithValue("$paper",archivePaperId);
        using var reader=cmd.ExecuteReader();
        var result=new List<WorkspaceArchiveRevision>();
        while(reader.Read())result.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2)));
        return result;
    }

    public IReadOnlyList<ResourceEntry> ListWorkspaceArchiveResources(string archivePaperId,
        int? revision=null)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT source_id,filename,role,location,size,sha256
            FROM workspace_archive_resources
            WHERE archive_paper_id=$paper AND revision=COALESCE($revision,(
                SELECT MAX(revision) FROM workspace_archive_revisions WHERE archive_paper_id=$paper))
            ORDER BY location,role,filename
            """;
        cmd.Parameters.AddWithValue("$paper",archivePaperId);
        cmd.Parameters.AddWithValue("$revision",revision is null?DBNull.Value:(object)revision.Value);
        using var reader=cmd.ExecuteReader();
        var result=new List<ResourceEntry>();
        while(reader.Read())result.Add(new(reader.GetString(0),reader.GetString(1),
            reader.GetString(2),reader.GetString(3),reader.GetInt64(4),reader.GetString(5)));
        return result;
    }

    public string? GetWorkspaceArchiveManifest(string archivePaperId,int revision)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT manifest_json,manifest_sha256 FROM workspace_archive_revisions
            WHERE archive_paper_id=$paper AND revision=$revision
            """;
        cmd.Parameters.AddWithValue("$paper",archivePaperId);
        cmd.Parameters.AddWithValue("$revision",revision);
        using var reader=cmd.ExecuteReader();
        if(!reader.Read())return null;
        var json=reader.GetString(0);
        var expected=reader.GetString(1);
        var computed=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
        if(computed!=expected)
            throw new InvalidDataException("归档清单的完整性校验失败。");
        return json;
    }

    public WorkspaceArchivePreview PrepareWorkspaceArchive(ExperimentWorkspace workspace,
        string paperId,string documentId)
    {
        if(Path.GetFullPath(workspace.Root).TrimEnd(Path.DirectorySeparatorChar)!=
           Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException("工作台与归档库必须使用相同的内容寻址仓库。");
        var paper=workspace.ListPaperProjects().SingleOrDefault(x=>x.Id==paperId)
            ??throw new KeyNotFoundException("论文项目不存在。");
        var document=workspace.ListPaperDocuments(paperId).SingleOrDefault(x=>x.Id==documentId)
            ??throw new InvalidOperationException("请选择当前论文项目下的论文全文。");
        var checks=new List<ArchiveCheck>();
        var resources=new List<ArchiveResourceSnapshot>();
        var figures=new List<object>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        void Add(string sourceId,string filename,string role,string location,string sha,long size)
        {
            var key=sourceId+"|"+role+"|"+location;
            if(seen.Add(key))resources.Add(new(sourceId,filename,role,location,sha,size));
        }
        void CheckAsset(ArchiveResourceSnapshot item)
        {
            if(item.Sha256.Length!=64||item.Sha256.Any(c=>!Uri.IsHexDigit(c)))
            {
                checks.Add(new("invalid_sha","block",item.FileName+" 的 SHA-256 无效。"));
                return;
            }
            var path=Path.Combine(_root,"blobs","sha256",item.Sha256[..2],item.Sha256);
            if(!File.Exists(path))
            {
                checks.Add(new("missing_blob","block",item.FileName+" 的归档原始字节缺失。"));
                return;
            }
            using var stream=File.OpenRead(path);
            var sha=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if(sha!=item.Sha256||stream.Length!=item.Size)
                checks.Add(new("corrupt_blob","block",item.FileName+" 的字节校验失败。"));
        }
        Add(document.Id,document.FileName,"manuscript","论文全文",document.Sha256,document.Size);
        var links=workspace.ListPaperFigureLinks(documentId);
        if(links.Count==0)checks.Add(new("no_figure","block","论文未确认关联任何 Figure。"));
        foreach(var link in links)
        {
            if(link.Status!="confirmed"||link.CompositeId is null)
            {
                checks.Add(new("unconfirmed_figure","block",
                    $"Figure {link.FigureNumber} 尚未绑定已确认的最终版本。"));
                continue;
            }
            var composite=workspace.ListFigureComposites(link.FigureId)
                .SingleOrDefault(x=>x.Id==link.CompositeId);
            if(composite is null)
            {
                checks.Add(new("missing_composite","block",$"Figure {link.FigureNumber} 成图版本不存在。"));
                continue;
            }
            var location=$"Figure {link.FigureNumber} / v{composite.Version}";
            Add(composite.Id,composite.FileName,"composite",location,composite.Sha256,composite.Size);
            var sources=workspace.ListCompositeSources(composite.Id);
            if(sources.Count==0)
                checks.Add(new("no_sources","review",location+" 尚未确认 Panel/SHOW 来源。"));
            var sourceList=new List<object>();
            foreach(var source in sources)
            {
                if(source.Status!="confirmed")
                    checks.Add(new("source_review","review",location+" Panel "+source.PanelLabel+" 的来源尚未确认。"));
                var show=workspace.GetProvenanceAsset(source.AssetId);
                if(show is null)
                {
                    checks.Add(new("missing_show","block",location+" 的 SHOW 来源不存在。"));
                    continue;
                }
                if(show.Sha256!=source.AssetSha256)
                    checks.Add(new("source_changed","block",location+" 来源 SHA-256 与历史快照不一致。"));
                var panelPath=location+" / Panel "+source.PanelLabel;
                Add(show.Id,show.FileName,"show",panelPath,source.AssetSha256,show.Size);
                var processList=new List<object>();
                foreach(var edge in workspace.ListAssetUpstream(show.Id))
                {
                    var process=workspace.GetProvenanceAsset(edge.SourceAssetId);
                    if(process is null)
                    {
                        checks.Add(new("missing_process","block",panelPath+" 上游 PROCESS 不存在。"));continue;
                    }
                    Add(process.Id,process.FileName,"process",panelPath,process.Sha256,process.Size);
                    if(edge.Status!="confirmed")
                        checks.Add(new("unconfirmed_process","review",process.FileName+" 尚未确认加工关系。"));
                    var rawList=new List<object>();
                    foreach(var rawEdge in workspace.ListAssetUpstream(process.Id))
                    {
                        var raw=workspace.GetProvenanceAsset(rawEdge.SourceAssetId);
                        if(raw is null)
                        {
                            checks.Add(new("missing_raw","block",process.FileName+" 上游 RAW 不存在。"));continue;
                        }
                        Add(raw.Id,raw.FileName,"raw",panelPath,raw.Sha256,raw.Size);
                        if(rawEdge.Status!="confirmed")
                            checks.Add(new("unconfirmed_raw","review",raw.FileName+" 尚未确认加工关系。"));
                        rawList.Add(new {raw.Id,raw.FileName,raw.Sha256,rawEdge.Operation,rawEdge.Status});
                    }
                    processList.Add(new {process.Id,process.FileName,process.Sha256,edge.Operation,
                        edge.Status,raw=rawList});
                }
                if(processList.Count==0)
                    checks.Add(new("untraced_show","review",show.FileName+" 未记录明确的 PROCESS 来源。"));
                sourceList.Add(new {source.PanelId,source.PanelLabel,show.Id,show.FileName,
                    source.AssetSha256,source.Status,process=processList});
            }
            figures.Add(new {link.FigureId,link.FigureNumber,composite.Id,composite.Version,
                composite.FileName,composite.Sha256,sources=sourceList});
        }
        foreach(var resource in resources)CheckAsset(resource);
        var manifest=JsonSerializer.Serialize(new {schemaVersion=1,paperId,paper.Name,
            document=new {document.Id,document.FileName,document.Sha256},
            figures,resources=resources.OrderBy(x=>x.Location,StringComparer.Ordinal)
                .ThenBy(x=>x.Role,StringComparer.Ordinal)
                .ThenBy(x=>x.SourceId,StringComparer.Ordinal).ToArray()});
        var manifestSha=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)))
            .ToLowerInvariant();
        return new(paperId,paper.Name,documentId,resources,checks,manifest,manifestSha,
            figures.Count);
    }

    // Publishing happens in a single archive.db transaction. An unpublished or
    // interrupted attempt never appears in the library, and retries are idempotent.
    public WorkspaceArchiveResult PublishWorkspaceArchive(WorkspaceArchivePreview preview,
        bool acknowledgeReviews=false)
    {
        if(!preview.CanPublish)
            throw new InvalidOperationException("归档预检存在阻断问题。");
        if(!acknowledgeReviews&&preview.Checks.Any(x=>x.Level=="review"))
            throw new InvalidOperationException("请确认所有待核对项后再归档。");
        // Refuse to publish stale checks; caller should regenerate preview just
        // before confirmation and compare the manifest hash.
        foreach(var item in preview.Resources)
        {
            var path=Path.Combine(_root,"blobs","sha256",item.Sha256[..2],item.Sha256);
            using var stream=File.OpenRead(path);
            if(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()!=item.Sha256||
               stream.Length!=item.Size)
                throw new InvalidDataException("归档提交时素材校验失败："+item.FileName);
        }
        using var db=Connect();
        using var tx=db.BeginTransaction();
        string paperId;
        using(var lookup=db.CreateCommand())
        {
            lookup.Transaction=tx;
            lookup.CommandText="SELECT archive_paper_id FROM workspace_archive_papers WHERE workspace_paper_id=$id";
            lookup.Parameters.AddWithValue("$id",preview.PaperId);
            paperId=lookup.ExecuteScalar() as string ?? "";
        }
        if(paperId=="")
        {
            paperId=Guid.CreateVersion7().ToString();
            Execute(db,tx,"""
                INSERT INTO papers(paper_id,title,created_at,updated_at,archive_status,
                    archive_revision,archived_at)
                VALUES($id,$title,$time,$time,'archived',1,$time)
                """,("$id",paperId),("$title",preview.Title),("$time",Now()));
            Execute(db,tx,"""
                INSERT INTO workspace_archive_papers(workspace_paper_id,archive_paper_id)
                VALUES($workspace,$archive)
                """,("$workspace",preview.PaperId),("$archive",paperId));
        }
        using(var dup=db.CreateCommand())
        {
            dup.Transaction=tx;
            dup.CommandText="""
                SELECT revision FROM workspace_archive_revisions
                WHERE archive_paper_id=$paper AND manifest_sha256=$sha
                """;
            dup.Parameters.AddWithValue("$paper",paperId);
            dup.Parameters.AddWithValue("$sha",preview.ManifestSha256);
            var exists=dup.ExecuteScalar();
            if(exists is not null)
            {
                tx.Commit();
                return new(paperId,Convert.ToInt32(exists),true,preview.ManifestSha256,
                    preview.Resources.Count);
            }
        }
        int revision;
        using(var next=db.CreateCommand())
        {
            next.Transaction=tx;
            next.CommandText="""
                SELECT COALESCE(MAX(revision),0)+1 FROM workspace_archive_revisions
                WHERE archive_paper_id=$paper
                """;
            next.Parameters.AddWithValue("$paper",paperId);
            revision=Convert.ToInt32(next.ExecuteScalar());
        }
        var now=Now();
        Execute(db,tx,"""
            INSERT INTO workspace_archive_revisions(
              archive_paper_id,revision,document_id,manifest_sha256,manifest_json,archived_at)
            VALUES($paper,$revision,$document,$sha,$json,$time)
            """,("$paper",paperId),("$revision",revision),
            ("$document",preview.DocumentId),("$sha",preview.ManifestSha256),
            ("$json",preview.ManifestJson),("$time",now));
        foreach(var resource in preview.Resources)
            Execute(db,tx,"""
                INSERT INTO workspace_archive_resources(
                    archive_paper_id,revision,source_id,filename,role,location,sha256,size)
                VALUES($paper,$revision,$id,$name,$role,$location,$sha,$size)
                """,("$paper",paperId),("$revision",revision),("$id",resource.SourceId),
                ("$name",resource.FileName),("$role",resource.Role),
                ("$location",resource.Location),("$sha",resource.Sha256),("$size",resource.Size));
        Execute(db,tx,"""
            UPDATE papers SET title=$title,archive_status='archived',
                archive_revision=$revision,archived_at=$time,updated_at=$time
            WHERE paper_id=$paper
            """,("$paper",paperId),("$title",preview.Title),("$revision",revision),("$time",now));
        tx.Commit();
        return new(paperId,revision,false,preview.ManifestSha256,preview.Resources.Count);
    }

    public WorkspaceArchiveResult ArchiveWorkspacePaper(ExperimentWorkspace workspace,
        string paperId,string documentId,bool acknowledgeReviews=false)
    {
        var preview=PrepareWorkspaceArchive(workspace,paperId,documentId);
        return PublishWorkspaceArchive(preview,acknowledgeReviews);
    }
}
