using Microsoft.Data.Sqlite;

namespace PaperArchive.Core;

public sealed record E5ArchivedSearchHit(string PaperId,string PaperName,
    int Revision,string FileName,string Role,string Location,string Sha256);

public sealed partial class ArchiveRepository
{
    /// <summary>
    /// Search only frozen revisions. Results have actual archive-backed
    /// content hashes and are never reconstructed from the current workspace.
    /// </summary>
    public IReadOnlyList<E5ArchivedSearchHit> SearchE5Archives(
        string query,int limit=80)
    {
        if(string.IsNullOrWhiteSpace(query))return [];
        var escaped=query.Trim().Replace("\\","\\\\")
            .Replace("%","\\%").Replace("_","\\_");
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT DISTINCT r.archive_paper_id,p.title,r.revision,
                   a.filename,a.role,a.location,a.sha256
            FROM workspace_archive_revisions r
            JOIN papers p ON p.paper_id=r.archive_paper_id
            JOIN workspace_archive_resources a
                ON a.archive_paper_id=r.archive_paper_id AND a.revision=r.revision
            WHERE p.title LIKE $pattern ESCAPE '\' COLLATE NOCASE
                OR a.filename LIKE $pattern ESCAPE '\' COLLATE NOCASE
                OR a.location LIKE $pattern ESCAPE '\' COLLATE NOCASE
            ORDER BY r.revision DESC,p.title COLLATE NOCASE,a.filename COLLATE NOCASE
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$pattern","%"+escaped+"%");
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,200));
        var hits=new List<E5ArchivedSearchHit>();
        using var reader=cmd.ExecuteReader();
        while(reader.Read())hits.Add(new(reader.GetString(0),reader.GetString(1),
            reader.GetInt32(2),reader.GetString(3),reader.GetString(4),
            reader.GetString(5),reader.GetString(6)));
        return hits;
    }
}
