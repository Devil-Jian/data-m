using Microsoft.Data.Sqlite;

namespace PaperArchive.Core;

public sealed record ArchivedAssetReference(
    string ArchivePaperId, int Revision, string FileName, string Role,
    string Location, string Sha256, string ArchivedAt);

public sealed partial class ArchiveRepository
{
    /// <summary>
    /// Returns only actual immutable archive-resource rows, never guessed
    /// from current workspace relations or same-sample naming conventions.
    /// Historical revisions remain independently queryable.
    /// </summary>
    public IReadOnlyList<ArchivedAssetReference> ListArchivedAssetReferences(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT r.archive_paper_id,r.revision,r.filename,r.role,r.location,
                   r.sha256,v.archived_at
            FROM workspace_archive_resources r
            JOIN workspace_archive_revisions v
              ON v.archive_paper_id=r.archive_paper_id AND v.revision=r.revision
            WHERE r.source_id=$asset
            ORDER BY v.archived_at DESC,r.archive_paper_id,r.revision DESC,r.location
            """;
        cmd.Parameters.AddWithValue("$asset",assetId);
        using var rd=cmd.ExecuteReader();
        var result=new List<ArchivedAssetReference>();
        while(rd.Read())
            result.Add(new(rd.GetString(0),rd.GetInt32(1),rd.GetString(2),
                rd.GetString(3),rd.GetString(4),rd.GetString(5),rd.GetString(6)));
        return result;
    }
}
