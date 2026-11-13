using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

/// <summary>Search result IDs are internal navigation targets, never UI labels.</summary>
public sealed record E5WorkspaceSearchHit(
    string Kind,string Title,string Context,string TargetId,string? ExperimentId);

public sealed partial class ExperimentWorkspace
{
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
