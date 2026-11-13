using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>Search result IDs are internal navigation targets, never UI labels.</summary>
public sealed record E5WorkspaceSearchHit(
    string Kind,string Title,string Context,string TargetId,string? ExperimentId);

public sealed partial class ExperimentWorkspace
{
    /// <summary>
    /// Current files only: E1 path mappings for folder-managed experiments,
    /// stable D2 sample references for unadopted legacy experiments. This
    /// excludes superseded source versions from everyday browsing.
    /// </summary>
    public IReadOnlyList<E4ExperimentAsset> ListE5CurrentExperimentFiles(
        string experimentId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            WITH chosen(experiment_id) AS (SELECT $experiment),
            files(id) AS (
                SELECT current.asset_id FROM chosen e
                JOIN e1_experiment_roots root
                  ON root.experiment_id=e.experiment_id
                JOIN e1_root_current_assets current ON current.root_id=root.id
                JOIN experiment_assets asset ON asset.id=current.asset_id
                JOIN e1_root_result_batches batch ON batch.result_id=asset.result_id
                  AND batch.root_id=root.id
                UNION
                SELECT membership.asset_id FROM chosen e
                JOIN sample_units sample ON sample.experiment_id=e.experiment_id
                JOIN sample_unit_assets membership
                  ON membership.sample_unit_id=sample.id
                WHERE NOT EXISTS(
                    SELECT 1 FROM e1_experiment_roots root
                    WHERE root.experiment_id=e.experiment_id)
            )
            SELECT asset.id,asset.filename,COALESCE(tags.stage,asset.stage),
                sample.id,sample.sample_name,asset.sha256,asset.size,
                asset.relative_path
            FROM files
            JOIN experiment_assets asset ON asset.id=files.id AND asset.is_deleted=0
            JOIN experiment_results batch ON batch.id=asset.result_id
                AND batch.is_deleted=0
            JOIN experiment_units unit ON unit.id=$experiment
                AND unit.project_id=batch.project_id
            LEFT JOIN asset_system_tags tags ON tags.asset_id=asset.id
            LEFT JOIN sample_unit_assets membership ON membership.asset_id=asset.id
            LEFT JOIN sample_units sample ON sample.id=membership.sample_unit_id
                AND sample.experiment_id=$experiment
            ORDER BY COALESCE(sample.sample_name,'') COLLATE NOCASE,
                asset.filename COLLATE NOCASE,asset.id
            """;
        cmd.Parameters.AddWithValue("$experiment",experimentId);
        var files=new List<E4ExperimentAsset>();
        using var rd=cmd.ExecuteReader();
        while(rd.Read())files.Add(new(
            rd.GetString(0),rd.GetString(1),rd.GetString(2),
            rd.IsDBNull(3)?null:rd.GetString(3),
            rd.IsDBNull(4)?null:rd.GetString(4),
            rd.GetString(5),rd.GetInt64(6),rd.GetString(7)));
        return files;
    }

    /// <summary>
    /// Bounded, case-insensitive search of indexed papers, experiments and
    /// files. Search only returns active source versions for E1-managed roots,
    /// without scanning all imported historical content in the UI thread.
    /// </summary>
    public IReadOnlyList<E5WorkspaceSearchHit> SearchE5Workspace(string query,int limit=80)
    {
        if(string.IsNullOrWhiteSpace(query))return [];
        limit=Math.Clamp(limit,1,200);
        // LIKE wildcards are escaped: user input is treated as literal text.
        var escaped=query.Trim().Replace("\\","\\\\")
            .Replace("%","\\%").Replace("_","\\_");
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT kind,title,context,target_id,experiment_id
            FROM (
                SELECT 'paper' kind,p.name title,'论文' context,p.id target_id,
                       NULL experiment_id
                FROM paper_projects p
                WHERE p.name LIKE $pattern ESCAPE '\' COLLATE NOCASE
                UNION ALL
                SELECT 'experiment',e.experiment_name,'实验资料库',e.id,
                       e.experiment_id
                FROM e1_experiment_roots e
                WHERE e.experiment_name LIKE $pattern ESCAPE '\' COLLATE NOCASE
                UNION ALL
                SELECT 'file',a.filename,e.experiment_name||' / '||
                    COALESCE(sample.sample_name,'其他文件'),a.id,
                    e.experiment_id
                FROM e1_root_current_assets current
                JOIN e1_experiment_roots e ON e.id=current.root_id
                JOIN experiment_assets a ON a.id=current.asset_id AND a.is_deleted=0
                JOIN experiment_results result ON result.id=a.result_id
                    AND result.is_deleted=0
                LEFT JOIN sample_unit_assets membership ON membership.asset_id=a.id
                LEFT JOIN sample_units sample ON sample.id=membership.sample_unit_id
                    AND sample.experiment_id=e.experiment_id
                WHERE a.filename LIKE $pattern ESCAPE '\' COLLATE NOCASE
            )
            ORDER BY kind, title COLLATE NOCASE, context COLLATE NOCASE
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$pattern","%"+escaped+"%");
        cmd.Parameters.AddWithValue("$limit",limit);
        var items=new List<E5WorkspaceSearchHit>();
        using var reader=cmd.ExecuteReader();
        while(reader.Read())items.Add(new(
            reader.GetString(0),reader.GetString(1),reader.GetString(2),
            reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4)));
        return items;
    }
}
