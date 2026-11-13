using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record E4ExperimentAsset(
    string AssetId,string FileName,string Stage,string? SampleId,string? SampleName,
    string Sha256,long Size,string RelativePath);

public sealed partial class ExperimentWorkspace
{
    /// <summary>
    /// Snapshot the CURRENT experiment membership for a Panel. For E1 roots,
    /// use the active relative-path index (not all historical imported versions).
    /// Legacy experiments without physical-root evidence fall back to stable D2
    /// sample memberships; no processing ancestry is invented.
    /// </summary>
    public IReadOnlyList<E4ExperimentAsset> ListE4PanelAssets(string panelId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            WITH chosen(experiment_id) AS (
                SELECT experiment_id FROM e2_panel_experiment_links
                WHERE panel_id=$panel
            ),
            assets(id) AS (
                SELECT c.asset_id
                FROM chosen e
                JOIN e1_experiment_roots root
                    ON root.experiment_id=e.experiment_id
                JOIN e1_root_current_assets c ON c.root_id=root.id
                JOIN e1_root_result_batches batches ON batches.result_id=(
                    SELECT a.result_id FROM experiment_assets a WHERE a.id=c.asset_id)
                    AND batches.root_id=root.id
                UNION
                SELECT m.asset_id
                FROM chosen e
                JOIN sample_units s ON s.experiment_id=e.experiment_id
                JOIN sample_unit_assets m ON m.sample_unit_id=s.id
                WHERE NOT EXISTS(
                    SELECT 1 FROM e1_experiment_roots root
                    WHERE root.experiment_id=e.experiment_id)
            )
            SELECT a.id,a.filename,COALESCE(t.stage,a.stage),
                   sample.id,sample.sample_name,a.sha256,a.size,a.relative_path
            FROM assets src
            JOIN experiment_assets a ON a.id=src.id AND a.is_deleted=0
            JOIN experiment_results r ON r.id=a.result_id AND r.is_deleted=0
            JOIN chosen e
            JOIN experiment_units experiment ON experiment.id=e.experiment_id
                AND experiment.project_id=r.project_id
            LEFT JOIN asset_system_tags t ON t.asset_id=a.id
            LEFT JOIN sample_unit_assets membership ON membership.asset_id=a.id
            LEFT JOIN sample_units sample ON sample.id=membership.sample_unit_id
                AND sample.experiment_id=e.experiment_id
            ORDER BY COALESCE(sample.sample_name,'') COLLATE NOCASE,
                a.filename COLLATE NOCASE,a.id
            """;
        cmd.Parameters.AddWithValue("$panel",panelId);
        var rows=new List<E4ExperimentAsset>();
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
            rows.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),
                reader.IsDBNull(3)?null:reader.GetString(3),
                reader.IsDBNull(4)?null:reader.GetString(4),
                reader.GetString(5),reader.GetInt64(6),reader.GetString(7)));
        return rows;
    }
}
