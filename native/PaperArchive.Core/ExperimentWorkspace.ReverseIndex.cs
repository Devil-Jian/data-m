using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>
/// A read-only reverse lookup. A working Panel reference is never represented
/// as evidence that a manuscript or an archived Figure used that image.
/// A candidate processing edge remains a candidate throughout the traversal.
/// </summary>
public sealed record AssetPaperReference(
    string AssetId, string ShowAssetId, string FigureId, int FigureNumber,
    string PanelId, string PanelLabel, string? CompositeId, int? CompositeVersion,
    string? DocumentId, string? DocumentName, string ReferenceKind,
    string SourceStatus, string ProcessingStatus, string? DocumentLinkStatus);

public sealed partial class ExperimentWorkspace
{
    /// <summary>
    /// From RAW / PROCESS / SHOW / QUANT, walk recorded processing edges forward,
    /// then discover actual working Panel links and immutable composite snapshots.
    /// No same-sample index inference is used to construct a paper relationship.
    /// </summary>
    public IReadOnlyList<AssetPaperReference> ListAssetPaperReferences(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            WITH RECURSIVE downstream(id,confidence,path,depth) AS (
              SELECT id,'confirmed','|' || id || '|',0
              FROM experiment_assets WHERE id=$asset
              UNION ALL
              SELECT e.target_asset_id,
                     CASE WHEN d.confidence='confirmed' AND e.status='confirmed'
                          THEN 'confirmed' ELSE 'candidate' END,
                     d.path || e.target_asset_id || '|',d.depth+1
              FROM downstream d
              JOIN asset_provenance_edges e ON e.source_asset_id=d.id
              WHERE d.depth<8
                AND instr(d.path,'|' || e.target_asset_id || '|')=0
            ),
            target_shows AS (
              SELECT id,
                 CASE WHEN MIN(CASE WHEN confidence='confirmed' THEN 0 ELSE 1 END)=0
                      THEN 'confirmed' ELSE 'candidate' END AS confidence
              FROM downstream GROUP BY id
            )
            SELECT $asset, s.id, f.id, f.number, p.id, p.label,
                   NULL,NULL,NULL,NULL,'working','current',s.confidence,NULL
            FROM target_shows s
            JOIN experiment_assets a ON a.id=s.id AND a.stage='show'
            JOIN figure_panel_assets l ON l.asset_id=s.id
            JOIN panel_templates p ON p.id=l.panel_id
            JOIN figure_templates f ON f.id=p.figure_id
            UNION ALL
            SELECT $asset,s.id,f.id,f.number,
                   COALESCE(h.panel_id,''),h.panel_label,
                   c.id,c.version,doc.id,doc.filename,
                   'snapshot',h.status,s.confidence,ref.status
            FROM target_shows s
            JOIN experiment_assets a ON a.id=s.id AND a.stage='show'
            JOIN figure_composite_source_snapshots h ON h.asset_id=s.id
            JOIN figure_composites c ON c.id=h.composite_id
            JOIN figure_templates f ON f.id=c.figure_id
            LEFT JOIN paper_document_figure_versions ref ON ref.composite_id=c.id
            LEFT JOIN paper_documents doc ON doc.id=ref.document_id
            ORDER BY 4,6,7,9
            """;
        cmd.Parameters.AddWithValue("$asset",assetId);
        var result=new List<AssetPaperReference>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())
            result.Add(new(
                rd.GetString(0),rd.GetString(1),rd.GetString(2),rd.GetInt32(3),
                rd.GetString(4),rd.GetString(5),
                rd.IsDBNull(6)?null:rd.GetString(6),
                rd.IsDBNull(7)?null:rd.GetInt32(7),
                rd.IsDBNull(8)?null:rd.GetString(8),
                rd.IsDBNull(9)?null:rd.GetString(9),
                rd.GetString(10),rd.GetString(11),rd.GetString(12),
                rd.IsDBNull(13)?null:rd.GetString(13)));
        return result;
    }

    /// <summary>
    /// Structured forward traversal of the exact Figure versions pinned by a
    /// manuscript; uses snapshots, not live Panel links.
    /// </summary>
    public IReadOnlyList<AssetPaperReference> ListDocumentAssetReferences(string documentId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT h.asset_id,h.asset_id,f.id,f.number,
                   COALESCE(h.panel_id,''),h.panel_label,
                   c.id,c.version,d.id,d.filename,
                   'snapshot',h.status,link.status,link.status
            FROM paper_document_figure_versions link
            JOIN paper_documents d ON d.id=link.document_id
            JOIN figure_templates f ON f.id=link.figure_id
            JOIN figure_composites c ON c.id=link.composite_id
            JOIN figure_composite_source_snapshots h ON h.composite_id=c.id
            WHERE link.document_id=$document
            ORDER BY f.number,h.panel_label,h.asset_id
            """;
        cmd.Parameters.AddWithValue("$document",documentId);
        var result=new List<AssetPaperReference>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())
            result.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
                rd.GetInt32(3),rd.GetString(4),rd.GetString(5),
                rd.GetString(6),rd.GetInt32(7),rd.GetString(8),rd.GetString(9),
                rd.GetString(10),rd.GetString(11),rd.GetString(12),
                rd.GetString(13)));
        return result;
    }
}
