using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record GlobalAssetSearchHit(
    ExperimentAsset Asset,string ResultName,string ProjectId,string ProjectName,
    string? PaperId,string? PaperName);

/// <summary>Cross-paper file lookup, independent of the currently loaded UI scope.</summary>
public sealed partial class ExperimentWorkspace
{
    public IReadOnlyList<GlobalAssetSearchHit> SearchAssetsGlobal(string text,
        int limit=100,int offset=0)
    {
        var term=text.Trim();
        if(term.Length<2)throw new ArgumentException("请至少输入 2 个字符。",nameof(text));
        if(term.Length>200)throw new ArgumentException("搜索词不可超过 200 个字符。",nameof(text));
        if(limit is <1 or >200)throw new ArgumentOutOfRangeException(nameof(limit));
        if(offset<0)throw new ArgumentOutOfRangeException(nameof(offset));
        // Escape wildcards: search is a literal substring match, never an SQL pattern.
        var pattern="%"+term.Replace("\\","\\\\",StringComparison.Ordinal)
            .Replace("%","\\%",StringComparison.Ordinal)
            .Replace("_","\\_",StringComparison.Ordinal)+"%";
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT a.id,a.result_id,a.group_id,g.prefix,a.stage,a.relative_path,
                   a.filename,a.size,a.sha256,r.name,p.id,p.name,p.paper_id,
                   COALESCE(paper.name,'')
            FROM experiment_assets a
            JOIN experiment_groups g ON g.id=a.group_id
            JOIN experiment_results r ON r.id=a.result_id
            JOIN experiment_projects p ON p.id=r.project_id
            LEFT JOIN paper_projects paper ON paper.id=p.paper_id
            WHERE a.is_deleted=0 AND r.is_deleted=0 AND p.is_deleted=0
              AND (
                a.filename LIKE $pattern ESCAPE '\' COLLATE NOCASE OR
                a.relative_path LIKE $pattern ESCAPE '\' COLLATE NOCASE OR
                g.prefix LIKE $pattern ESCAPE '\' COLLATE NOCASE OR
                a.id LIKE $pattern ESCAPE '\' COLLATE NOCASE OR
                r.name LIKE $pattern ESCAPE '\' COLLATE NOCASE OR
                p.name LIKE $pattern ESCAPE '\' COLLATE NOCASE OR
                paper.name LIKE $pattern ESCAPE '\' COLLATE NOCASE
              )
            ORDER BY a.filename COLLATE NOCASE,a.id
            LIMIT $limit OFFSET $offset
            """;
        cmd.Parameters.AddWithValue("$pattern",pattern);
        cmd.Parameters.AddWithValue("$limit",limit);
        cmd.Parameters.AddWithValue("$offset",offset);
        using var reader=cmd.ExecuteReader();
        var hits=new List<GlobalAssetSearchHit>();
        while(reader.Read())
        {
            var asset=new ExperimentAsset(reader.GetString(0),reader.GetString(1),
                reader.GetString(2),reader.GetString(3),reader.GetString(4),
                reader.GetString(5),reader.GetString(6),reader.GetInt64(7),
                reader.GetString(8));
            hits.Add(new(asset,reader.GetString(9),reader.GetString(10),
                reader.GetString(11),reader.IsDBNull(12)?null:reader.GetString(12),
                reader.GetString(13)));
        }
        return hits;
    }
}
