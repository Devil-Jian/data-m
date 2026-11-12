using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record PanelSampleUse(string PanelId,string SampleUnitId,
    string ExperimentName,string SampleName,string ShowAssetId,string ShowFileName);
public sealed record CompositeSampleUse(string CompositeId,string PanelId,
    string PanelLabel,string SampleUnitId,string ExperimentName,
    string SampleName,string ShowAssetId,string ShowSha256);

public sealed partial class ExperimentWorkspace
{
    private void InitializePanelSampleLinks()
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS panel_sample_links(
                panel_id TEXT NOT NULL REFERENCES panel_templates(id) ON DELETE CASCADE,
                sample_unit_id TEXT NOT NULL REFERENCES sample_units(id),
                show_asset_id TEXT NOT NULL REFERENCES experiment_assets(id),
                created_panel_link_id TEXT,
                created_at TEXT NOT NULL,
                PRIMARY KEY(panel_id,sample_unit_id)
            );
            CREATE INDEX IF NOT EXISTS ix_panel_sample_links_show
                ON panel_sample_links(show_asset_id);
            CREATE TABLE IF NOT EXISTS composite_sample_source_snapshots(
                composite_id TEXT NOT NULL REFERENCES figure_composites(id),
                panel_id TEXT NOT NULL,
                panel_label TEXT NOT NULL,
                sample_unit_id TEXT NOT NULL,
                experiment_name TEXT NOT NULL,
                sample_name TEXT NOT NULL,
                show_asset_id TEXT NOT NULL,
                show_sha256 TEXT NOT NULL,
                PRIMARY KEY(composite_id,panel_id,sample_unit_id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<ExperimentAsset> ListSelectableSampleShows(string sampleUnitId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,a.relative_path,
                   a.filename,a.size,a.sha256
            FROM sample_unit_assets m
            JOIN experiment_assets a ON a.id=m.asset_id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN experiment_projects p ON p.id=r.project_id AND p.is_deleted=0
            JOIN asset_system_tags t ON t.asset_id=a.id
            JOIN experiment_groups g ON g.id=a.group_id
            WHERE m.sample_unit_id=$sample AND a.stage='show'
              AND t.stage='show' AND t.parse_status='resolved'
            ORDER BY a.filename COLLATE NOCASE,a.id
            """;
        cmd.Parameters.AddWithValue("$sample",sampleUnitId);
        var assets=new List<ExperimentAsset>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())assets.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),
            rd.GetString(5),rd.GetString(6),rd.GetInt64(7),rd.GetString(8)));
        return assets;
    }

    public PanelSampleUse LinkSampleToPanel(string panelId,string sampleUnitId,
        string? chosenShowAssetId=null)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        var owner=GetPanelOwner(db,tx,panelId);
        using var query=db.CreateCommand();
        query.Transaction=tx;
        query.CommandText="""
            SELECT a.id,a.filename,e.name,s.sample_name,a.result_id,a.stage,
                   t.stage,t.parse_status,p.paper_id
            FROM sample_unit_assets m
            JOIN sample_units s ON s.id=m.sample_unit_id
            JOIN experiment_units e ON e.id=s.experiment_id
            JOIN experiment_assets a ON a.id=m.asset_id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN experiment_projects p ON p.id=r.project_id AND p.is_deleted=0
            JOIN asset_system_tags t ON t.asset_id=a.id
            WHERE s.id=$sample AND a.stage='show' AND t.stage='show'
              AND t.parse_status='resolved'
            ORDER BY a.filename COLLATE NOCASE,a.id
            """;
        query.Parameters.AddWithValue("$sample",sampleUnitId);
        var shows=new List<(string Id,string Name,string Experiment,string Sample,
            string? PaperId)>();
        using(var rd=query.ExecuteReader())
            while(rd.Read())shows.Add((rd.GetString(0),rd.GetString(1),
                rd.GetString(2),rd.GetString(3),
                rd.IsDBNull(8)?null:rd.GetString(8)));
        var eligible=shows.Where(x=>x.PaperId==owner.PaperId).ToArray();
        if(eligible.Length==0)
            throw new InvalidOperationException(
                "该样本没有属于当前论文的有效 SHOW；请检查实验项目归属、阶段及解析状态。");
        if(chosenShowAssetId is null && eligible.Length>1)
            throw new InvalidOperationException(
                "样本含多张 SHOW，请先选择需要用于该 Panel 的确切文件版本。");
        var chosen=chosenShowAssetId is null?eligible[0]:
            eligible.SingleOrDefault(x=>x.Id==chosenShowAssetId);
        if(chosen.Id is null)
            throw new InvalidOperationException("所选 SHOW 不属于此样本或当前论文。");
        using(var existing=db.CreateCommand())
        {
            existing.Transaction=tx;
            existing.CommandText="""
                SELECT show_asset_id FROM panel_sample_links
                WHERE panel_id=$panel AND sample_unit_id=$sample
                """;
            existing.Parameters.AddWithValue("$panel",panelId);
            existing.Parameters.AddWithValue("$sample",sampleUnitId);
            if(existing.ExecuteScalar() is string selected)
            {
                if(selected!=chosen.Id)
                    throw new InvalidOperationException(
                        "该样本已绑定其他 SHOW；请先取消现有样本关联，再选择新版本。");
                return new(panelId,sampleUnitId,chosen.Experiment,chosen.Sample,
                    chosen.Id,chosen.Name);
            }
        }
        var createdLinkId=NewId();
        using(var link=db.CreateCommand())
        {
            link.Transaction=tx;
            link.CommandText="""
                INSERT OR IGNORE INTO figure_panel_assets(id,panel_id,asset_id,position,created_at)
                VALUES($id,$panel,$asset,
                    (SELECT COALESCE(MAX(position),0)+1 FROM figure_panel_assets WHERE panel_id=$panel),
                    $now)
                """;
            link.Parameters.AddWithValue("$id",createdLinkId);
            link.Parameters.AddWithValue("$panel",panelId);
            link.Parameters.AddWithValue("$asset",chosen.Id);
            link.Parameters.AddWithValue("$now",UtcNow());
            if(link.ExecuteNonQuery()==0)createdLinkId="";
        }
        using(var add=db.CreateCommand())
        {
            add.Transaction=tx;
            add.CommandText="""
                INSERT INTO panel_sample_links(
                    panel_id,sample_unit_id,show_asset_id,created_panel_link_id,created_at)
                VALUES($panel,$sample,$show,$link,$now)
                """;
            add.Parameters.AddWithValue("$panel",panelId);
            add.Parameters.AddWithValue("$sample",sampleUnitId);
            add.Parameters.AddWithValue("$show",chosen.Id);
            add.Parameters.AddWithValue("$link",
                createdLinkId.Length==0?DBNull.Value:createdLinkId);
            add.Parameters.AddWithValue("$now",UtcNow());
            add.ExecuteNonQuery();
        }
        tx.Commit();
        return new(panelId,sampleUnitId,chosen.Experiment,chosen.Sample,chosen.Id,chosen.Name);
    }

    public IReadOnlyList<PanelSampleUse> ListPanelSampleUses(string panelId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT l.panel_id,l.sample_unit_id,e.name,s.sample_name,
                   l.show_asset_id,a.filename
            FROM panel_sample_links l
            JOIN sample_units s ON s.id=l.sample_unit_id
            JOIN experiment_units e ON e.id=s.experiment_id
            JOIN experiment_assets a ON a.id=l.show_asset_id
            WHERE l.panel_id=$panel
            ORDER BY e.name COLLATE NOCASE,s.sample_name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$panel",panelId);
        var result=new List<PanelSampleUse>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),rd.GetString(5)));
        return result;
    }

    public bool UnlinkSampleFromPanel(string panelId,string sampleUnitId)
    {
        using var db=Connect();
        using var tx=db.BeginTransaction();
        string? ownedLink;
        using(var find=db.CreateCommand())
        {
            find.Transaction=tx;
            find.CommandText="""
                SELECT created_panel_link_id FROM panel_sample_links
                WHERE panel_id=$panel AND sample_unit_id=$sample
                """;
            find.Parameters.AddWithValue("$panel",panelId);
            find.Parameters.AddWithValue("$sample",sampleUnitId);
            using var rd=find.ExecuteReader();
            if(!rd.Read())return false;
            ownedLink=rd.IsDBNull(0)?null:rd.GetString(0);
        }
        using(var remove=db.CreateCommand())
        {
            remove.Transaction=tx;
            remove.CommandText="""
                DELETE FROM panel_sample_links WHERE panel_id=$panel AND sample_unit_id=$sample
                """;
            remove.Parameters.AddWithValue("$panel",panelId);
            remove.Parameters.AddWithValue("$sample",sampleUnitId);
            remove.ExecuteNonQuery();
        }
        if(ownedLink is not null)
        {
            // If another sample uses the same SHOW in this Panel, pass along
            // ownership of the automatically-created link for final cleanup.
            using(var transfer=db.CreateCommand())
            {
                transfer.Transaction=tx;
                transfer.CommandText="""
                    UPDATE panel_sample_links SET created_panel_link_id=$link
                    WHERE panel_id=$panel AND show_asset_id=(
                      SELECT asset_id FROM figure_panel_assets WHERE id=$link)
                      AND created_panel_link_id IS NULL
                """;
                transfer.Parameters.AddWithValue("$link",ownedLink);
                transfer.Parameters.AddWithValue("$panel",panelId);
                transfer.ExecuteNonQuery();
            }
            using var clean=db.CreateCommand();
            clean.Transaction=tx;
            clean.CommandText="""
                DELETE FROM figure_panel_assets WHERE id=$link
                  AND NOT EXISTS(SELECT 1 FROM panel_sample_links
                    WHERE panel_id=$panel AND show_asset_id=figure_panel_assets.asset_id)
                """;
            clean.Parameters.AddWithValue("$link",ownedLink);
            clean.Parameters.AddWithValue("$panel",panelId);
            clean.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    private static void CaptureCompositeSampleUses(SqliteConnection db,
        SqliteTransaction tx,string figureId,string compositeId)
    {
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="""
            INSERT INTO composite_sample_source_snapshots(
                composite_id,panel_id,panel_label,sample_unit_id,
                experiment_name,sample_name,show_asset_id,show_sha256)
            SELECT $composite,p.id,p.label,s.id,e.name,s.sample_name,a.id,a.sha256
            FROM panel_sample_links l
            JOIN panel_templates p ON p.id=l.panel_id
            JOIN sample_units s ON s.id=l.sample_unit_id
            JOIN experiment_units e ON e.id=s.experiment_id
            JOIN experiment_assets a ON a.id=l.show_asset_id
            JOIN figure_panel_assets fpa ON fpa.panel_id=p.id AND fpa.asset_id=a.id
            WHERE p.figure_id=$figure
            """;
        cmd.Parameters.AddWithValue("$figure",figureId);
        cmd.Parameters.AddWithValue("$composite",compositeId);
        cmd.ExecuteNonQuery();
    }

    public sealed record DocumentSampleReference(
        string DocumentId,string FigureId,int FigureNumber,string CompositeId,
        int CompositeVersion,string PanelId,string PanelLabel,string ExperimentName,
        string SampleName,string SampleUnitId,string ShowAssetId,string ShowSha256);

    /// <summary>Manuscript references the pinned composite snapshot, never live panels.</summary>
    public IReadOnlyList<DocumentSampleReference> ListDocumentSampleReferences(
        string documentId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT l.document_id,f.id,f.number,c.id,c.version,
                   s.panel_id,s.panel_label,s.experiment_name,s.sample_name,
                   s.sample_unit_id,s.show_asset_id,s.show_sha256
            FROM paper_document_figure_versions l
            JOIN figure_templates f ON f.id=l.figure_id
            JOIN figure_composites c ON c.id=l.composite_id
            JOIN composite_sample_source_snapshots s ON s.composite_id=c.id
            WHERE l.document_id=$document AND l.status='confirmed'
            ORDER BY f.number,c.version,s.panel_label,s.sample_name
            """;
        cmd.Parameters.AddWithValue("$document",documentId);
        var result=new List<DocumentSampleReference>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())result.Add(new(
            rd.GetString(0),rd.GetString(1),rd.GetInt32(2),rd.GetString(3),
            rd.GetInt32(4),rd.GetString(5),rd.GetString(6),rd.GetString(7),
            rd.GetString(8),rd.GetString(9),rd.GetString(10),rd.GetString(11)));
        return result;
    }

    public IReadOnlyList<CompositeSampleUse> ListCompositeSampleUses(string compositeId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT composite_id,panel_id,panel_label,sample_unit_id,
                experiment_name,sample_name,show_asset_id,show_sha256
            FROM composite_sample_source_snapshots
            WHERE composite_id=$id ORDER BY panel_label,sample_name
            """;
        cmd.Parameters.AddWithValue("$id",compositeId);
        var rows=new List<CompositeSampleUse>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())rows.Add(new(rd.GetString(0),rd.GetString(1),
            rd.GetString(2),rd.GetString(3),rd.GetString(4),
            rd.GetString(5),rd.GetString(6),rd.GetString(7)));
        return rows;
    }
}
