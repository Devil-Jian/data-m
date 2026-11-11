using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed partial class ExperimentWorkspace
{
    /// <summary>
    /// A Figure drop creates one next-available Panel per SHOW asset. Validate
    /// all assets first and then create Panel and link rows in ONE transaction.
    /// Nothing is copied, moved, or inferred about processing provenance.
    /// </summary>
    public IReadOnlyList<PanelTemplate> LinkShowAssetsToNewPanels(
        string figureId, IEnumerable<string> assetIds)
    {
        var ids = assetIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return [];
        using var db = Connect();
        using var tx = db.BeginTransaction();
        string paperId;
        using (var query = db.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = "SELECT paper_id FROM figure_templates WHERE id=$figure";
            query.Parameters.AddWithValue("$figure", figureId);
            paperId = query.ExecuteScalar() as string
                ?? throw new KeyNotFoundException("Figure 不存在。");
        }

        // Validate the entire batch BEFORE creating the first Panel.
        foreach (var id in ids)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT COUNT(*) FROM experiment_assets a
                JOIN experiment_results r ON r.id=a.result_id
                JOIN experiment_projects p ON p.id=r.project_id
                WHERE a.id=$asset AND a.stage='show' AND a.is_deleted=0
                  AND r.is_deleted=0 AND p.is_deleted=0 AND p.paper_id=$paper
                """;
            cmd.Parameters.AddWithValue("$asset", id);
            cmd.Parameters.AddWithValue("$paper", paperId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
                throw new InvalidOperationException("只能将当前论文项目内的 SHOW 图片拖入 Figure。");
        }
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var query = db.CreateCommand())
        {
            query.Transaction = tx;
            query.CommandText = "SELECT label FROM panel_templates WHERE figure_id=$figure";
            query.Parameters.AddWithValue("$figure", figureId);
            using var rd = query.ExecuteReader();
            while (rd.Read()) existing.Add(rd.GetString(0));
        }
        var greatest = existing.Where(x => x.Length == 1 && x[0] >= 'A' && x[0] <= 'Z')
            .Select(x => x[0]).DefaultIfEmpty('@').Max();
        var free = Enumerable.Range('A',26).Select(x => ((char)x).ToString())
            .Where(x => !existing.Contains(x)).ToArray();
        var labels = free.Where(x => x[0] > greatest)
            .Concat(free.Where(x => x[0] <= greatest)).Take(ids.Length).ToArray();
        if (labels.Length != ids.Length)
            throw new InvalidOperationException("Figure 的 Panel A–Z 位置不足，未创建任何关联。");

        var created = new List<PanelTemplate>();
        var createdLinks = new List<FigureDropLink>();
        for (var i = 0; i < ids.Length; i++)
        {
            InsertPanel(db, tx, figureId, labels[i]);
            string panelId;
            using (var query = db.CreateCommand())
            {
                query.Transaction = tx;
                query.CommandText = """
                    SELECT id FROM panel_templates WHERE figure_id=$figure AND label=$label
                    """;
                query.Parameters.AddWithValue("$figure", figureId);
                query.Parameters.AddWithValue("$label", labels[i]);
                panelId = query.ExecuteScalar() as string ??
                    throw new InvalidDataException("新建 Panel 记录不存在。");
            }
            using (var link = db.CreateCommand())
            {
                link.Transaction = tx;
                link.CommandText = """
                    INSERT INTO figure_panel_assets(
                        id,panel_id,asset_id,position,created_at)
                    VALUES($id,$panel,$asset,1,$now)
                    """;
                var linkId = NewId();
                link.Parameters.AddWithValue("$id", linkId);
                link.Parameters.AddWithValue("$panel", panelId);
                link.Parameters.AddWithValue("$asset", ids[i]);
                link.Parameters.AddWithValue("$now", UtcNow());
                link.ExecuteNonQuery();
                createdLinks.Add(new(linkId, panelId, ids[i]));
            }
            created.Add(new(panelId, figureId, labels[i]));
        }
        RecordFigureDropUndo(db, tx, new(figureId, createdLinks.ToArray(),
            created.Select(x => new FigureDropPanel(x.Id, x.Label)).ToArray()));
        tx.Commit();
        return created;
    }

    private sealed record FigureDropLink(string Id, string PanelId, string AssetId);
    private sealed record FigureDropPanel(string Id, string Label);
    private sealed record FigureDropBatch(
        string FigureId, FigureDropLink[] Links, FigureDropPanel[] CreatedPanels);

    private void InitializeFigureDropUndo()
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS figure_drop_undo (
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                figure_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL,
                undone INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_figure_drop_undo_figure
                ON figure_drop_undo(figure_id,seq);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void RecordFigureDropUndo(SqliteConnection db, SqliteTransaction tx,
        FigureDropBatch batch)
    {
        if (batch.Links.Length == 0) return;
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO figure_drop_undo(figure_id,payload,created_at)
            VALUES($figure,$payload,$now)
            """;
        cmd.Parameters.AddWithValue("$figure", batch.FigureId);
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(batch));
        cmd.Parameters.AddWithValue("$now", UtcNow());
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Restore only the links/panels created by the last Figure drop. If any
    /// created Panel/link has been edited, abort without modifying anything.
    /// Historical composite source snapshots and blobs are never changed.
    /// </summary>
    public bool UndoLastFigureDrop(string figureId)
    {
        using var db = Connect();
        using var tx = db.BeginTransaction();
        long seq;
        FigureDropBatch batch;
        using (var get = db.CreateCommand())
        {
            get.Transaction = tx;
            get.CommandText = """
                SELECT seq,payload FROM figure_drop_undo
                WHERE figure_id=$figure AND undone=0 ORDER BY seq DESC LIMIT 1
                """;
            get.Parameters.AddWithValue("$figure", figureId);
            using var rd = get.ExecuteReader();
            if (!rd.Read()) return false;
            seq = rd.GetInt64(0);
            batch = JsonSerializer.Deserialize<FigureDropBatch>(rd.GetString(1))
                ?? throw new InvalidDataException("组图撤销记录损坏。");
        }
        if (batch.FigureId != figureId)
            throw new InvalidDataException("组图撤销记录的 Figure 不一致。");

        foreach (var link in batch.Links)
        {
            using var verify = db.CreateCommand();
            verify.Transaction = tx;
            verify.CommandText = """
                SELECT COUNT(*) FROM figure_panel_assets
                WHERE id=$id AND panel_id=$panel AND asset_id=$asset
                """;
            verify.Parameters.AddWithValue("$id", link.Id);
            verify.Parameters.AddWithValue("$panel", link.PanelId);
            verify.Parameters.AddWithValue("$asset", link.AssetId);
            if (Convert.ToInt64(verify.ExecuteScalar()) != 1)
                throw new InvalidOperationException("组图链接已被修改，不能撤销较早的拖拽。");
        }
        foreach (var panel in batch.CreatedPanels)
        {
            using (var verify = db.CreateCommand())
            {
                verify.Transaction = tx;
                verify.CommandText = """
                    SELECT COUNT(*) FROM panel_templates
                    WHERE id=$id AND figure_id=$figure AND label=$label
                    """;
                verify.Parameters.AddWithValue("$id", panel.Id);
                verify.Parameters.AddWithValue("$figure", figureId);
                verify.Parameters.AddWithValue("$label", panel.Label);
                if (Convert.ToInt64(verify.ExecuteScalar()) != 1)
                    throw new InvalidOperationException("Panel 已重命名或移动，不能自动撤销。");
            }
            using (var count = db.CreateCommand())
            {
                count.Transaction = tx;
                count.CommandText = "SELECT COUNT(*) FROM figure_panel_assets WHERE panel_id=$panel";
                count.Parameters.AddWithValue("$panel", panel.Id);
                if (Convert.ToInt64(count.ExecuteScalar()) !=
                    batch.Links.Count(x => x.PanelId == panel.Id))
                    throw new InvalidOperationException("新建 Panel 已增加其他图片，不能自动撤销。");
            }
        }
        foreach (var link in batch.Links)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM figure_panel_assets WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", link.Id);
            cmd.ExecuteNonQuery();
        }
        foreach (var panel in batch.CreatedPanels)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM panel_templates WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", panel.Id);
            cmd.ExecuteNonQuery();
        }
        using (var done = db.CreateCommand())
        {
            done.Transaction = tx;
            done.CommandText = "UPDATE figure_drop_undo SET undone=1 WHERE seq=$seq";
            done.Parameters.AddWithValue("$seq", seq);
            done.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }
}
