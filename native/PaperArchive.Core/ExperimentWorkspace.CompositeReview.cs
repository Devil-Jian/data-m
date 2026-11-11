using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record CompositeSourceReview(
    string CompositeId, string FigureId, bool IsLivePanelCandidate,
    string Fingerprint, IReadOnlyList<CompositeSourceSnapshot> Sources);

public sealed partial class ExperimentWorkspace
{
    // For a legacy composite without source snapshots, only PREVIEW today's
    // Panel links. They are explicitly marked review-only and are not history.
    private static CompositeSourceReview ReadCompositeSourceReview(
        SqliteConnection db, SqliteTransaction tx, string compositeId)
    {
        string figureId;
        using (var find = db.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT figure_id FROM figure_composites WHERE id=$id";
            find.Parameters.AddWithValue("$id", compositeId);
            figureId = find.ExecuteScalar() as string ??
                throw new KeyNotFoundException("完整 Figure 版本不存在。");
        }
        var rows = new List<CompositeSourceSnapshot>();
        using (var history = db.CreateCommand())
        {
            history.Transaction = tx;
            history.CommandText = """
                SELECT panel_id,panel_label,asset_id,asset_sha256,status
                FROM figure_composite_source_snapshots
                WHERE composite_id=$id ORDER BY panel_label,asset_id
                """;
            history.Parameters.AddWithValue("$id", compositeId);
            using var rd = history.ExecuteReader();
            while (rd.Read())
                rows.Add(new(compositeId,
                    rd.IsDBNull(0) ? null : rd.GetString(0), rd.GetString(1),
                    rd.GetString(2), rd.GetString(3), rd.GetString(4)));
        }
        var liveCandidate = rows.Count == 0;
        if (liveCandidate)
        {
            using var query = db.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """
                SELECT p.id,p.label,a.id,a.sha256
                FROM figure_panel_assets l
                JOIN panel_templates p ON p.id=l.panel_id
                JOIN experiment_assets a ON a.id=l.asset_id
                WHERE p.figure_id=$figure ORDER BY p.label,a.id
                """;
            query.Parameters.AddWithValue("$figure", figureId);
            using var rd = query.ExecuteReader();
            while (rd.Read())
                rows.Add(new(compositeId,rd.GetString(0),rd.GetString(1),
                    rd.GetString(2),rd.GetString(3),"review"));
        }
        // Used as an optimistic lock: reopening the dialog after edits
        // guarantees that the user confirms exactly what was reviewed.
        var canonical = compositeId + "|" + (liveCandidate ? "live" : "snapshot")
            + "\n" + string.Join("\n", rows.Select(x =>
                x.PanelId + "|" + x.PanelLabel + "|" + x.AssetId + "|" +
                x.AssetSha256 + "|" + x.Status));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(compositeId,figureId,liveCandidate,fingerprint,rows);
    }

    public CompositeSourceReview PreviewCompositeSourceReview(string compositeId)
    {
        using var db = Connect();
        using var tx = db.BeginTransaction();
        var review = ReadCompositeSourceReview(db,tx,compositeId);
        tx.Commit();
        return review;
    }

    /// <summary>
    /// One explicit user confirmation: if the historic version has no snapshot,
    /// capture current Panel links as REVIEW candidates only after approval,
    /// then mark those exact candidates confirmed in the same transaction.
    /// No old snapshot is overwritten and no processing edges are inferred.
    /// </summary>
    public void ConfirmCompositeSourceReview(string compositeId,string expectedFingerprint)
    {
        using var db = Connect();
        using var tx = db.BeginTransaction();
        var review = ReadCompositeSourceReview(db,tx,compositeId);
        if (!string.Equals(expectedFingerprint,review.Fingerprint,StringComparison.Ordinal))
            throw new InvalidOperationException("Panel/SHOW 来源已变化，请重新核对后确认。");
        if (review.Sources.Count == 0)
            throw new InvalidOperationException("此版本没有可核对的 Panel/SHOW 来源，请先关联 SHOW 图片。");
        if (review.IsLivePanelCandidate)
            CaptureCompositeSources(db,tx,review.FigureId,compositeId,"review");
        using (var confirm = db.CreateCommand())
        {
            confirm.Transaction = tx;
            confirm.CommandText = """
                UPDATE figure_composite_source_snapshots SET status='confirmed'
                WHERE composite_id=$id
                """;
            confirm.Parameters.AddWithValue("$id",compositeId);
            if (confirm.ExecuteNonQuery() != review.Sources.Count)
                throw new InvalidDataException("来源快照数量不一致，已撤销确认。");
        }
        tx.Commit();
    }
}
