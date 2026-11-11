using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

// V15-15: relationships are separate from files and from current working Panel links.
public sealed record PaperFigureLink(
    string DocumentId,string FigureId,int FigureNumber,string? CompositeId,
    int? Version,string Status,string Source,string ReferenceLabel);
public sealed record CompositeSourceSnapshot(
    string CompositeId,string? PanelId,string PanelLabel,string AssetId,
    string AssetSha256,string Status);
public sealed record AssetProvenanceEdge(string SourceAssetId,string TargetAssetId,
    string Operation,string Status);
public sealed record SourceAssetCandidate(ExperimentAsset Asset,string Relation);
public sealed record ManuscriptReferenceCandidate(int FigureNumber,string? PanelLabel);

public sealed partial class ExperimentWorkspace
{
    private void InitializeProvenance()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS paper_document_figure_versions(
                document_id TEXT NOT NULL REFERENCES paper_documents(id),
                figure_id TEXT NOT NULL REFERENCES figure_templates(id),
                composite_id TEXT REFERENCES figure_composites(id),
                status TEXT NOT NULL CHECK(status IN ('candidate','confirmed','review')),
                source TEXT NOT NULL CHECK(source IN ('detected','manual')),
                reference_label TEXT NOT NULL,
                confirmed_at TEXT,
                PRIMARY KEY(document_id,figure_id)
            );
            CREATE INDEX IF NOT EXISTS idx_doc_fig_composite ON paper_document_figure_versions(composite_id);
            CREATE TABLE IF NOT EXISTS figure_composite_source_snapshots(
                composite_id TEXT NOT NULL REFERENCES figure_composites(id),
                panel_id TEXT,
                panel_label TEXT NOT NULL,
                asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                asset_sha256 TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('candidate','confirmed','review')),
                captured_at TEXT NOT NULL,
                PRIMARY KEY(composite_id,panel_label,asset_id)
            );
            CREATE TABLE IF NOT EXISTS asset_provenance_edges(
                source_asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                target_asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                operation TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('candidate','confirmed')),
                created_at TEXT NOT NULL,
                PRIMARY KEY(source_asset_id,target_asset_id)
            );
            CREATE INDEX IF NOT EXISTS idx_provenance_target ON asset_provenance_edges(target_asset_id);
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PaperFigureLink> ListPaperFigureLinks(string documentId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT l.document_id,l.figure_id,f.number,l.composite_id,c.version,
                   l.status,l.source,l.reference_label
            FROM paper_document_figure_versions l
            JOIN figure_templates f ON f.id=l.figure_id
            LEFT JOIN figure_composites c ON c.id=l.composite_id
            WHERE l.document_id=$id ORDER BY f.number
            """;
        cmd.Parameters.AddWithValue("$id",documentId);
        using var reader=cmd.ExecuteReader();
        var result=new List<PaperFigureLink>();
        while(reader.Read())result.Add(new(reader.GetString(0),reader.GetString(1),
            reader.GetInt32(2),reader.IsDBNull(3)?null:reader.GetString(3),
            reader.IsDBNull(4)?null:reader.GetInt32(4),
            reader.GetString(5),reader.GetString(6),reader.GetString(7)));
        return result;
    }

    // Binding a paper never silently changes its previously confirmed composite.
    // A deliberate user action replaces the document's selected Figure version.
    public void SetPaperFigureLink(string documentId,string figureId,
        string? compositeId,bool confirmed,string source="manual")
    {
        if(source is not ("manual" or "detected"))throw new ArgumentException("无效来源。",nameof(source));
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var check=db.CreateCommand();
        check.Transaction=tx;
        check.CommandText="""
            SELECT f.number FROM figure_templates f
            JOIN paper_documents d ON d.paper_id=f.paper_id
            WHERE f.id=$figure AND d.id=$document AND d.is_deleted=0
            """;
        check.Parameters.AddWithValue("$document",documentId);
        check.Parameters.AddWithValue("$figure",figureId);
        var number=check.ExecuteScalar();
        if(number is null)throw new InvalidOperationException("论文与 Figure 不属于同一论文项目。");
        if(compositeId is not null)
        {
            using var verify=db.CreateCommand();
            verify.Transaction=tx;
            verify.CommandText="SELECT COUNT(*) FROM figure_composites WHERE id=$id AND figure_id=$figure";
            verify.Parameters.AddWithValue("$id",compositeId);
            verify.Parameters.AddWithValue("$figure",figureId);
            if(Convert.ToInt64(verify.ExecuteScalar())!=1)
                throw new InvalidOperationException("指定版本不属于当前 Figure。");
        }
        var status=confirmed?(compositeId is null?"review":"confirmed"):"candidate";
        using var command=db.CreateCommand();
        command.Transaction=tx;
        command.CommandText="""
            INSERT INTO paper_document_figure_versions(
                document_id,figure_id,composite_id,status,source,reference_label,confirmed_at)
            VALUES($document,$figure,$composite,$status,$source,$label,$time)
            ON CONFLICT(document_id,figure_id) DO UPDATE SET
                composite_id=excluded.composite_id,status=excluded.status,
                source=excluded.source,reference_label=excluded.reference_label,
                confirmed_at=excluded.confirmed_at
            """;
        command.Parameters.AddWithValue("$document",documentId);
        command.Parameters.AddWithValue("$figure",figureId);
        command.Parameters.AddWithValue("$composite",(object?)compositeId??DBNull.Value);
        command.Parameters.AddWithValue("$status",status);
        command.Parameters.AddWithValue("$source",source);
        command.Parameters.AddWithValue("$label","Figure "+number);
        command.Parameters.AddWithValue("$time",confirmed?(object)UtcNow():DBNull.Value);
        command.ExecuteNonQuery();
        tx.Commit();
    }

    public void RemovePaperFigureLink(string documentId,string figureId)
    {
        using var db=Connect();
        using var command=db.CreateCommand();
        command.CommandText="DELETE FROM paper_document_figure_versions WHERE document_id=$doc AND figure_id=$figure";
        command.Parameters.AddWithValue("$doc",documentId);
        command.Parameters.AddWithValue("$figure",figureId);
        command.ExecuteNonQuery();
    }

    // Generate candidate references only from unambiguous text in DOCX. PDF/DOC
    // references can be bound manually; never assert a scanned PDF's contents.
    public IReadOnlyList<ManuscriptReferenceCandidate> DetectManuscriptFigureReferences(PaperDocument document)
    {
        if(!document.FileName.EndsWith(".docx",StringComparison.OrdinalIgnoreCase))
            return [];
        using var archive=ZipFile.OpenRead(GetPaperDocumentPath(document));
        var entry=archive.GetEntry("word/document.xml");
        if(entry is null)return [];
        using var stream=entry.Open();
        var xml=XDocument.Load(stream);
        var words=string.Join(" ",xml.Descendants()
            .Where(x=>x.Name.LocalName=="t").Select(x=>x.Value));
        return Regex.Matches(words,@"(?i)\b(?:fig(?:ure)?\.?|图)\s*(\d+)\s*([A-Z])?\b")
            .Select(m=>new ManuscriptReferenceCandidate(int.Parse(m.Groups[1].Value),
                m.Groups[2].Success?m.Groups[2].Value.ToUpperInvariant():null))
            .Distinct().ToArray();
    }

    public int SuggestDocumentFigures(PaperDocument document)
    {
        var candidates=DetectManuscriptFigureReferences(document);
        var figures=ListFigures(document.PaperId).ToDictionary(f=>f.Number);
        var existing=ListPaperFigureLinks(document.Id).Select(x=>x.FigureId).ToHashSet();
        var added=0;
        foreach(var number in candidates.Select(x=>x.FigureNumber).Distinct())
        {
            if(!figures.TryGetValue(number,out var figure)||existing.Contains(figure.Id))continue;
            SetPaperFigureLink(document.Id,figure.Id,null,false,"detected");
            added++;
        }
        return added;
    }

    public IReadOnlyList<CompositeSourceSnapshot> ListCompositeSources(string compositeId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT composite_id,panel_id,panel_label,asset_id,asset_sha256,status
            FROM figure_composite_source_snapshots
            WHERE composite_id=$id ORDER BY panel_label,asset_id
            """;
        cmd.Parameters.AddWithValue("$id",compositeId);
        using var reader=cmd.ExecuteReader();
        var rows=new List<CompositeSourceSnapshot>();
        while(reader.Read())rows.Add(new(reader.GetString(0),
            reader.IsDBNull(1)?null:reader.GetString(1),
            reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5)));
        return rows;
    }

    // Captured at import; changing live Panel links never rewrites history.
    private static void CaptureCompositeSources(SqliteConnection db,
        SqliteTransaction tx,string figureId,string compositeId,string status="candidate")
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO figure_composite_source_snapshots(
                composite_id,panel_id,panel_label,asset_id,asset_sha256,status,captured_at)
            SELECT $composite,p.id,p.label,a.id,a.sha256,$status,$now
            FROM figure_panel_assets l
            JOIN panel_templates p ON p.id=l.panel_id
            JOIN experiment_assets a ON a.id=l.asset_id
            WHERE p.figure_id=$figure
            """;
        cmd.Parameters.AddWithValue("$composite",compositeId);
        cmd.Parameters.AddWithValue("$figure",figureId);
        cmd.Parameters.AddWithValue("$now",UtcNow());
        cmd.Parameters.AddWithValue("$status",status);
        cmd.ExecuteNonQuery();
    }

    // Old composite versions have no captured inputs. Never silently turn live
    // Panel links into historical evidence: backfill is explicitly review-only.
    public int SuggestLegacyCompositeSources(string compositeId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var check=db.CreateCommand();
        check.Transaction=tx;
        check.CommandText="SELECT figure_id FROM figure_composites WHERE id=$id";
        check.Parameters.AddWithValue("$id",compositeId);
        var figureId=check.ExecuteScalar() as string ??
            throw new KeyNotFoundException("完整成图版本不存在。");
        using var existing=db.CreateCommand();
        existing.Transaction=tx;
        existing.CommandText="SELECT COUNT(*) FROM figure_composite_source_snapshots WHERE composite_id=$id";
        existing.Parameters.AddWithValue("$id",compositeId);
        if(Convert.ToInt32(existing.ExecuteScalar())>0)
            throw new InvalidOperationException("此版本已有来源记录，不能覆盖历史快照。");
        CaptureCompositeSources(db,tx,figureId,compositeId,"review");
        using var count=db.CreateCommand();
        count.Transaction=tx;
        count.CommandText="SELECT COUNT(*) FROM figure_composite_source_snapshots WHERE composite_id=$id";
        count.Parameters.AddWithValue("$id",compositeId);
        var result=Convert.ToInt32(count.ExecuteScalar());
        tx.Commit();
        return result;
    }

    public void ConfirmCompositeSources(string compositeId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            UPDATE figure_composite_source_snapshots SET status='confirmed'
            WHERE composite_id=$id
            """;
        cmd.Parameters.AddWithValue("$id",compositeId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AssetProvenanceEdge> ListAssetUpstream(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT source_asset_id,target_asset_id,operation,status
            FROM asset_provenance_edges WHERE target_asset_id=$id
            ORDER BY status DESC,source_asset_id
            """;
        cmd.Parameters.AddWithValue("$id",assetId);
        using var rd=cmd.ExecuteReader();
        var result=new List<AssetProvenanceEdge>();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3)));
        return result;
    }

    public IReadOnlyList<SourceAssetCandidate> ListSameGroupUpstreamCandidates(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,a.relative_path,
                   a.filename,a.size,a.sha256
            FROM experiment_assets target
            JOIN experiment_assets a ON a.id<>target.id AND a.is_deleted=0
            JOIN experiment_groups g ON g.id=a.group_id
            LEFT JOIN sample_asset_index mine ON mine.asset_id=target.id
            LEFT JOIN sample_asset_index peer ON peer.asset_id=a.id
            WHERE target.id=$id
              AND (a.group_id=target.group_id
                OR (mine.asset_id IS NOT NULL AND peer.asset_id IS NOT NULL
                    AND mine.project_id=peer.project_id
                    AND mine.sample_key=peer.sample_key COLLATE NOCASE))
              AND
                  ((target.stage='show' AND a.stage='process')
                   OR (target.stage='process' AND a.stage='raw')
                   OR (target.stage='quant' AND a.stage IN ('raw','process')))
            ORDER BY a.stage,a.filename
            """;
        cmd.Parameters.AddWithValue("$id",assetId);
        using var rd=cmd.ExecuteReader();
        var result=new List<SourceAssetCandidate>();
        while(rd.Read())result.Add(new(new ExperimentAsset(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),rd.GetString(5),
            rd.GetString(6),rd.GetInt64(7),rd.GetString(8)),"同样本候选（未确认加工关系）"));
        return result;
    }

    public void SetAssetProvenance(string sourceAssetId,string targetAssetId,string operation,
        bool confirmed)
    {
        if(sourceAssetId==targetAssetId)throw new ArgumentException("输入输出不能是同一素材。");
        using var db=Connect();
        using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            SELECT id,group_id,stage FROM experiment_assets
            WHERE id IN ($source,$target) AND is_deleted=0
            """;
        cmd.Parameters.AddWithValue("$source",sourceAssetId);
        cmd.Parameters.AddWithValue("$target",targetAssetId);
        using var rd=cmd.ExecuteReader();
        var rows=new Dictionary<string,(string Group,string Stage)>();
        while(rd.Read())rows.Add(rd.GetString(0),(rd.GetString(1),rd.GetString(2)));
        rd.Close();
        if(!rows.TryGetValue(sourceAssetId,out var source)||
           !rows.TryGetValue(targetAssetId,out var target))
            throw new KeyNotFoundException("素材不存在。");
        if(source.Group!=target.Group||
           !(source.Stage=="raw"&&target.Stage=="process"||
             source.Stage=="process"&&target.Stage=="show"||
             (source.Stage=="raw"||source.Stage=="process")&&target.Stage=="quant"))
            throw new InvalidOperationException("只允许同组 RAW→PROCESS、PROCESS→SHOW，以及 RAW/PROCESS→QUANT 来源关联。");
        using var insert=db.CreateCommand();
        insert.Transaction=tx;
        insert.CommandText="""
            INSERT INTO asset_provenance_edges(source_asset_id,target_asset_id,operation,status,created_at)
            VALUES($source,$target,$operation,$status,$now)
            ON CONFLICT(source_asset_id,target_asset_id) DO UPDATE SET
                operation=excluded.operation,status=excluded.status
            """;
        insert.Parameters.AddWithValue("$source",sourceAssetId);
        insert.Parameters.AddWithValue("$target",targetAssetId);
        insert.Parameters.AddWithValue("$operation",operation.Trim());
        insert.Parameters.AddWithValue("$status",confirmed?"confirmed":"candidate");
        insert.Parameters.AddWithValue("$now",UtcNow());
        insert.ExecuteNonQuery();
        tx.Commit();
    }

    public ExperimentAsset? GetProvenanceAsset(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,
                   a.relative_path,a.filename,a.size,a.sha256
            FROM experiment_assets a JOIN experiment_groups g ON a.group_id=g.id
            WHERE a.id=$id
            """;
        cmd.Parameters.AddWithValue("$id",assetId);
        using var rd=cmd.ExecuteReader();
        return rd.Read()?new ExperimentAsset(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),rd.GetString(5),
            rd.GetString(6),rd.GetInt64(7),rd.GetString(8)):null;
    }

    private string ProvenanceAssetName(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT filename FROM experiment_assets WHERE id=$id";
        cmd.Parameters.AddWithValue("$id",assetId);
        return cmd.ExecuteScalar() as string ?? "[已缺失素材]";
    }

    public string DescribeDocumentProvenance(string documentId)
    {
        var lines=new List<string>();
        foreach(var link in ListPaperFigureLinks(documentId))
        {
            lines.Add($"Figure {link.FigureNumber} · {(link.Version is null?"未指定版本":"v"+link.Version)} · "+
                (link.Status=="confirmed"?"已确认":"待核对"));
            if(link.CompositeId is null)continue;
            var sources=ListCompositeSources(link.CompositeId);
            if(sources.Count==0)
                lines.Add("  无已记录的 Panel/SHOW 来源（需要核对）");
            foreach(var source in sources)
            {
                lines.Add($"  Panel {source.PanelLabel} → SHOW {ProvenanceAssetName(source.AssetId)} "+
                    $"[{(source.Status=="confirmed"?"已确认":"来源候选")}]");
                var upstream=ListAssetUpstream(source.AssetId);
                foreach(var process in upstream)
                {
                    lines.Add($"    ← PROCESS {ProvenanceAssetName(process.SourceAssetId)} "+
                        $"[{(process.Status=="confirmed"?"已确认":"待核对")}]");
                    foreach(var raw in ListAssetUpstream(process.SourceAssetId))
                        lines.Add($"      ← RAW {ProvenanceAssetName(raw.SourceAssetId)} "+
                            $"[{(raw.Status=="confirmed"?"已确认":"待核对")}]");
                }
                if(upstream.Count==0)
                {
                    var candidates=ListSameGroupUpstreamCandidates(source.AssetId);
                    foreach(var candidate in candidates)
                        lines.Add($"    ? PROCESS {candidate.Asset.FileName} [同组候选，未确认]");
                }
            }
        }
        return lines.Count==0?"尚未关联 Figure。":string.Join(Environment.NewLine,lines);
    }
}
