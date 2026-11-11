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
                link.Parameters.AddWithValue("$id", NewId());
                link.Parameters.AddWithValue("$panel", panelId);
                link.Parameters.AddWithValue("$asset", ids[i]);
                link.Parameters.AddWithValue("$now", UtcNow());
                link.ExecuteNonQuery();
            }
            created.Add(new(panelId, figureId, labels[i]));
        }
        tx.Commit();
        return created;
    }
}
