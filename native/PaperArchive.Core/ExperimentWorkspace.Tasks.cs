using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>
/// V1.6: read-only, derived tasks. Saving a file or a relationship automatically
/// changes the task state; there is no manual "mark complete" flag to maintain.
/// </summary>
public sealed record PaperWorkTask(string Code,string Title,string Description,
    string ActionLabel,string? DocumentId=null,string? FigureId=null,
    bool BlocksArchive=true);

public sealed record PaperWorkSummary(string PaperId,string Title,
    PaperDocument? PrimaryDocument,int DocumentCount,int FigureCount,
    int FinishedFigureCount,int LinkedFigureCount,int ConfirmedFigureCount,
    int AssetCount,IReadOnlyList<PaperWorkTask> Tasks)
{
    public int BlockingCount=>Tasks.Count(x=>x.BlocksArchive);
    public int ReviewCount=>Tasks.Count(x=>!x.BlocksArchive);
    public bool Ready=>BlockingCount==0;
}

public sealed partial class ExperimentWorkspace
{
    private void InitializePaperWorkPreferences()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS paper_work_preferences(
              paper_id TEXT PRIMARY KEY REFERENCES paper_projects(id),
              primary_document_id TEXT REFERENCES paper_documents(id),
              updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public PaperDocument? GetPrimaryPaperDocument(string paperId)
    {
        var documents=ListPaperDocuments(paperId);
        if(documents.Count==0)return null;
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT primary_document_id FROM paper_work_preferences WHERE paper_id=$paper";
        cmd.Parameters.AddWithValue("$paper",paperId);
        var id=cmd.ExecuteScalar() as string;
        // One imported document is unambiguous, without requiring user clicks.
        // An invalid persisted selection must never quietly pick another draft.
        return id is null?(documents.Count==1?documents[0]:null):
            documents.FirstOrDefault(x=>x.Id==id);
    }

    public void SetPrimaryPaperDocument(string paperId,string documentId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var check=db.CreateCommand();
        check.Transaction=tx;
        check.CommandText="""
            SELECT COUNT(*) FROM paper_documents
            WHERE id=$document AND paper_id=$paper AND is_deleted=0
            """;
        check.Parameters.AddWithValue("$paper",paperId);
        check.Parameters.AddWithValue("$document",documentId);
        if(Convert.ToInt64(check.ExecuteScalar())!=1)
            throw new InvalidOperationException("主文稿必须属于当前论文项目。");
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO paper_work_preferences(paper_id,primary_document_id,updated_at)
            VALUES($paper,$document,$now)
            ON CONFLICT(paper_id) DO UPDATE SET
              primary_document_id=excluded.primary_document_id,
              updated_at=excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$paper",paperId);
        cmd.Parameters.AddWithValue("$document",documentId);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public PaperWorkSummary GetPaperWorkSummary(string paperId)
    {
        var paper=ListPaperProjects().FirstOrDefault(x=>x.Id==paperId)
            ??throw new KeyNotFoundException("论文项目不存在。");
        var documents=ListPaperDocuments(paperId);
        var primary=GetPrimaryPaperDocument(paperId);
        var figures=ListFigures(paperId);
        var finished=figures.Count(x=>ListFigureComposites(x.Id).Count>0);
        var assetCount=ListProjectsForPaper(paperId)
            .Sum(x=>ListResults(x.Id).Sum(r=>r.FileCount));
        var tasks=new List<PaperWorkTask>();
        if(documents.Count==0)
            tasks.Add(new("import_manuscript","导入论文全文",
                "尚未导入 DOCX/PDF 论文全文。","导入全文"));
        else if(primary is null)
            tasks.Add(new("select_primary","指定论文主文稿",
                $"发现 {documents.Count} 份论文文件，请选择实际用于本次归档的版本。","选择主文稿"));

        int linked=0,confirmed=0;
        if(primary is not null)
        {
            var links=ListPaperFigureLinks(primary.Id);
            linked=links.Count;
            confirmed=links.Count(x=>x.Status=="confirmed" && x.CompositeId is not null);
            if(links.Count==0)
                tasks.Add(new("link_figures","确认论文使用的 Figure",
                    figures.Count==0?"尚未建立 Figure，请先创建或导入完整成图。":
                    $"项目已有 {figures.Count} 个 Figure，论文尚未绑定实际采用的版本。",
                    figures.Count==0?"进入组图":"关联 Figure",primary.Id));
            foreach(var link in links)
            {
                var figure=figures.FirstOrDefault(x=>x.Id==link.FigureId);
                var composite=figure is null?null:ListFigureComposites(figure.Id)
                    .FirstOrDefault(x=>x.Id==link.CompositeId);
                if(link.Status!="confirmed"||composite is null)
                {
                    tasks.Add(new("figure_version",
                        $"Figure {link.FigureNumber}：确认论文采用版本",
                        composite is null?"未指定或找不到完整成图版本。":
                        $"推荐候选为 v{composite.Version}，请确认论文实际采用版本。",
                        "选择版本",primary.Id,link.FigureId));
                    continue;
                }
                var sources=ListCompositeSources(composite.Id);
                var needsReview=sources.Count==0 || sources.Any(x=>x.Status!="confirmed");
                if(!needsReview)
                    needsReview=sources.Any(x=>
                    {
                        var upstream=ListAssetUpstream(x.AssetId);
                        return upstream.Count==0 ||
                            upstream.Any(edge=>edge.Status!="confirmed" ||
                                ListAssetUpstream(edge.SourceAssetId)
                                    .Any(raw=>raw.Status!="confirmed"));
                    });
                if(needsReview)
                    tasks.Add(new("review_sources",$"Figure {link.FigureNumber}：核对数据来源",
                        sources.Count==0?"成图版本没有记录 Panel/SHOW 来源。":
                        "来源快照或 PROCESS/RAW 加工关系仍有待核对项。",
                        "查看来源",primary.Id,link.FigureId,false));
            }
        }
        if(assetCount==0)
            tasks.Add(new("import_experiment","导入实验数据",
                "尚未导入实验素材；可先建立论文和组图，实验数据随后补充。",
                "导入素材",null,null,false));
        return new(paperId,paper.Name,primary,documents.Count,figures.Count,finished,
            linked,confirmed,assetCount,tasks);
    }
}
