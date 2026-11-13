using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>
/// E0 is deliberately read-only. It reports whether legacy Panel/Sample/SHOW
/// relationships can be mapped into the future strict Panel↔Experiment model.
/// Applying a migration belongs to E1/E2 and requires a separate backup step.
/// </summary>
public sealed record E0BaselineCounts(
    int Projects,int Results,int Experiments,int Samples,int Assets,
    int IndexedAssets,int UntaggedAssets,int Panels,int PanelSampleLinks,
    int PanelShowLinks,int FigureVersions,int HistoricalSourceSnapshots,
    int DocumentFigureLinks);
public sealed record E0ExperimentIdentity(
    string ExperimentId,string ProjectId,string ExperimentName,
    int ResultCount,int RootFolderResultCount,int LegacyResultCount,
    int IndexedAssetCount,string Evidence);
public sealed record E0PanelCandidate(
    string PanelId,string FigureId,int FigureNumber,string PanelLabel,
    IReadOnlyList<string> ExperimentIds,IReadOnlyList<string> ExperimentNames,
    int LegacySampleLinks,int LegacyShowLinks,int UnmappedShowCount,
    string Decision);
public sealed record E0MigrationIssue(string Code,string EntityId,string Message);
public sealed record E0MigrationPreview(
    E0BaselineCounts Counts,string BaselineFingerprint,
    IReadOnlyList<E0ExperimentIdentity> Experiments,
    IReadOnlyList<E0PanelCandidate> Panels,
    IReadOnlyList<E0MigrationIssue> Issues)
{
    public int AutoMappablePanels=>Panels.Count(x=>x.Decision=="ready");
    public int ConflictedPanels=>Panels.Count(x=>x.Decision=="multiple_experiments"
        ||x.Decision=="experiment_reused");
}

/// <summary>Inspect existing schema without INSERT/UPDATE/DELETE/CREATE.</summary>
public sealed partial class ExperimentWorkspace
{
    public E0MigrationPreview PreviewE0Migration()
    {
        using var db=Connect();
        // A read transaction makes the counts, candidate edges and fingerprint
        // describe the same database moment without writing any persistent row.
        using var tx=db.BeginTransaction(deferred:true);
        long Count(string sql)
        {
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText=sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        int N(string sql)=>checked((int)Count(sql));
        var counts=new E0BaselineCounts(
            N("SELECT COUNT(*) FROM experiment_projects WHERE is_deleted=0"),
            N("SELECT COUNT(*) FROM experiment_results WHERE is_deleted=0"),
            N("SELECT COUNT(*) FROM experiment_units"),
            N("SELECT COUNT(*) FROM sample_units"),
            N("SELECT COUNT(*) FROM experiment_assets WHERE is_deleted=0"),
            N("SELECT COUNT(*) FROM sample_unit_assets"),
            N("""
                SELECT COUNT(*) FROM experiment_assets a
                JOIN experiment_results r ON r.id=a.result_id
                LEFT JOIN asset_system_tags t ON t.asset_id=a.id
                WHERE a.is_deleted=0 AND r.is_deleted=0 AND t.asset_id IS NULL
                """),
            N("SELECT COUNT(*) FROM panel_templates"),
            N("SELECT COUNT(*) FROM panel_sample_links"),
            N("SELECT COUNT(*) FROM figure_panel_assets"),
            N("SELECT COUNT(*) FROM figure_composites"),
            N("SELECT COUNT(*) FROM figure_composite_source_snapshots"),
            N("SELECT COUNT(*) FROM paper_document_figure_versions"));

        var experiments=new List<E0ExperimentIdentity>();
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="""
                SELECT e.id,e.project_id,e.name,
                  COUNT(DISTINCT r.id),
                  COUNT(DISTINCT CASE WHEN l.source_kind='root_folder' THEN r.id END),
                  COUNT(DISTINCT CASE WHEN l.source_kind='legacy_fallback' OR
                    l.result_id IS NULL THEN r.id END),
                  COUNT(DISTINCT m.asset_id)
                FROM experiment_units e
                LEFT JOIN asset_system_tags t ON t.experiment_name=e.name COLLATE NOCASE
                LEFT JOIN experiment_assets a ON a.id=t.asset_id AND a.is_deleted=0
                LEFT JOIN experiment_results r ON r.id=a.result_id
                    AND r.project_id=e.project_id AND r.is_deleted=0
                LEFT JOIN experiment_result_labels l ON l.result_id=r.id
                LEFT JOIN sample_unit_assets m ON m.asset_id=a.id
                LEFT JOIN sample_units s ON s.id=m.sample_unit_id
                    AND s.experiment_id=e.id
                GROUP BY e.id,e.project_id,e.name
                ORDER BY e.project_id,e.name COLLATE NOCASE,e.id
                """;
            using var rd=cmd.ExecuteReader();
            while(rd.Read())
            {
                var resultCount=rd.GetInt32(3);
                var rooted=rd.GetInt32(4);
                var legacy=rd.GetInt32(5);
                var evidence=resultCount==0?"no_active_source":
                    resultCount>1?"multiple_result_roots":
                    rooted==1&&legacy==0?"root_folder_label":"legacy_or_mixed_label";
                experiments.Add(new(rd.GetString(0),rd.GetString(1),
                    rd.GetString(2),resultCount,rooted,legacy,
                    rd.GetInt32(6),evidence));
            }
        }

        // Legacy Panel links may have been created by dragging a Sample (D3)
        // or by dragging a SHOW directly (D1-D2). Both paths count as evidence.
        // Absence of indexed samples is an unresolved mapping, not permission
        // to manufacture a new Experiment identity from the filename.
        var linked=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        void Add(string panel,string experiment)
        {
            if(!linked.TryGetValue(panel,out var set))
                linked[panel]=set=new HashSet<string>(StringComparer.Ordinal);
            set.Add(experiment);
        }
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="""
                SELECT DISTINCT l.panel_id,s.experiment_id
                FROM panel_sample_links l
                JOIN sample_units s ON s.id=l.sample_unit_id
                UNION
                SELECT DISTINCT pa.panel_id,s.experiment_id
                FROM figure_panel_assets pa
                JOIN sample_unit_assets m ON m.asset_id=pa.asset_id
                JOIN sample_units s ON s.id=m.sample_unit_id
                """;
            using var rd=cmd.ExecuteReader();
            while(rd.Read())Add(rd.GetString(0),rd.GetString(1));
        }
        var names=experiments.ToDictionary(x=>x.ExperimentId,x=>x.ExperimentName,
            StringComparer.Ordinal);
        var panels=new List<E0PanelCandidate>();
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText="""
                SELECT p.id,p.figure_id,f.number,p.label,
                   (SELECT COUNT(*) FROM panel_sample_links x WHERE x.panel_id=p.id),
                   (SELECT COUNT(*) FROM figure_panel_assets x WHERE x.panel_id=p.id),
                   (SELECT COUNT(*) FROM figure_panel_assets x
                    LEFT JOIN sample_unit_assets m ON m.asset_id=x.asset_id
                    WHERE x.panel_id=p.id AND m.asset_id IS NULL)
                FROM panel_templates p
                JOIN figure_templates f ON f.id=p.figure_id
                ORDER BY f.paper_id,f.number,p.label COLLATE NOCASE,p.id
                """;
            using var rd=cmd.ExecuteReader();
            while(rd.Read())
            {
                var panelId=rd.GetString(0);
                var ids=linked.GetValueOrDefault(panelId)?
                    .OrderBy(x=>x,StringComparer.Ordinal).ToArray()??[]:
                    Array.Empty<string>();
                var unmapped=rd.GetInt32(6);
                var decision=ids.Length>1?"multiple_experiments":
                    unmapped>0?"unresolved_assets":
                    ids.Length==1?"ready":"empty";
                panels.Add(new(panelId,rd.GetString(1),rd.GetInt32(2),
                    rd.GetString(3),ids,ids.Select(id=>
                        names.GetValueOrDefault(id,"未知实验")).ToArray(),
                    rd.GetInt32(4),rd.GetInt32(5),unmapped,decision));
            }
        }

        var issues=new List<E0MigrationIssue>();
        foreach(var panel in panels)
        {
            if(panel.Decision=="multiple_experiments")
                issues.Add(new("panel_multiple_experiments",panel.PanelId,
                    $"Figure {panel.FigureNumber}-{panel.PanelLabel} 涉及多个实验，需选择一个。"));
            if(panel.Decision=="unresolved_assets")
                issues.Add(new("panel_unmapped_show",panel.PanelId,
                    $"Figure {panel.FigureNumber}-{panel.PanelLabel} 有 {panel.UnmappedShowCount} 张未建立稳定样本身份的 SHOW。"));
        }
        // If one Experiment is linked from more than one Panel, ALL such
        // candidates are blocked: automatically choosing a winner loses data.
        foreach(var group in panels.SelectMany(p=>p.ExperimentIds
                    .Select(id=>(ExperimentId:id,Panel:p)))
                 .GroupBy(x=>x.ExperimentId,StringComparer.Ordinal)
                 .Where(g=>g.Select(x=>x.Panel.PanelId).Distinct().Count()>1))
        {
            issues.Add(new("experiment_reused",group.Key,
                $"实验 {names.GetValueOrDefault(group.Key,"未知实验")} 已被 {group.Count()} 个 Panel 引用，禁止自动生成一对一关系。"));
            for(var i=0;i<panels.Count;i++)
                if(panels[i].ExperimentIds.Contains(group.Key))
                    panels[i]=panels[i] with {Decision="experiment_reused"};
        }
        foreach(var experiment in experiments)
        {
            if(experiment.Evidence!="root_folder_label")
                issues.Add(new("experiment_identity_uncertain",experiment.ExperimentId,
                    $"实验 {experiment.ExperimentName} 来源身份：{experiment.Evidence}。需在 E1 确认文件夹根目录身份，不凭名称合并。"));
        }
        if(counts.UntaggedAssets>0)
            issues.Add(new("legacy_untagged_assets","workspace",
                $"{counts.UntaggedAssets} 个有效文件尚无系统标签，需受控迁移。"));

        var fingerprint=ComputeE0Fingerprint(db,tx);
        tx.Commit();
        return new(counts,fingerprint,experiments,panels,issues);
    }

    private static string ComputeE0Fingerprint(SqliteConnection db,
        SqliteTransaction tx)
    {
        // Independent of row insertion order. No file bytes are read or changed.
        // The baseline covers IDs, asset content hashes and historical refs.
        using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var (table,order) in new[]{
            ("experiment_projects","id"),("experiment_results","id"),
            ("experiment_units","id"),("sample_units","id"),
            ("sample_unit_assets","asset_id"),("experiment_assets","id"),
            ("asset_system_tags","asset_id"),("panel_templates","id"),
            ("panel_sample_links","panel_id,sample_unit_id"),
            ("figure_panel_assets","id"),("figure_composites","id"),
            ("figure_composite_source_snapshots","composite_id,panel_label,asset_id"),
            ("composite_sample_source_snapshots","composite_id,panel_id,sample_unit_id"),
            ("paper_document_figure_versions","document_id,figure_id")
        })
        {
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText=$"SELECT * FROM {table} ORDER BY {order}";
            using var rd=cmd.ExecuteReader();
            sha.AppendData(Encoding.UTF8.GetBytes(table+"\n"));
            while(rd.Read())
            {
                for(var i=0;i<rd.FieldCount;i++)
                {
                    var value=rd.IsDBNull(i)?"<null>":rd.GetValue(i).ToString()??"";
                    // Length prefixes prevent field boundary ambiguities.
                    var bytes=Encoding.UTF8.GetBytes(value);
                    sha.AppendData(BitConverter.GetBytes(bytes.Length));
                    sha.AppendData(bytes);
                }
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
