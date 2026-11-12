using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed record SampleIndexChange(
    string AssetId, string FileName, string? PreviousSample, string? ProposedSample);
public sealed record SampleIndexPreview(
    string ProjectId, string Fingerprint, int IndexedCount, int UnresolvedProcessCount,
    IReadOnlyList<SampleIndexChange> Changes);

/// <summary>
/// An optional, project-scoped sample lookup. It never changes experiment_groups,
/// asset IDs, SHA-256 blobs, provenance edges, or immutable archive revisions.
/// Matching a sample is not evidence of a processing sequence.
/// </summary>
public sealed partial class ExperimentWorkspace
{
    private sealed record SampleIndexRow(string AssetId, string SampleKey, string MatchKind);
    private sealed record SampleIndexSource(
        string AssetId, string FileName, string Stage, string GroupId, string ResultId);
    private sealed record SampleIndexPlan(
        SampleIndexPreview Preview, SampleIndexRow[] Current, SampleIndexRow[] Proposed);

    private void InitializeSampleIndex()
    {
        using var db = Connect();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sample_asset_index (
                asset_id TEXT PRIMARY KEY REFERENCES experiment_assets(id),
                project_id TEXT NOT NULL REFERENCES experiment_projects(id),
                sample_key TEXT NOT NULL COLLATE NOCASE,
                match_kind TEXT NOT NULL CHECK(match_kind IN ('anchor','inferred'))
            );
            CREATE INDEX IF NOT EXISTS ix_sample_asset_index_key
                ON sample_asset_index(project_id,sample_key);
            CREATE TABLE IF NOT EXISTS sample_index_rebuild_undo (
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                previous_json TEXT NOT NULL,
                applied_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                undone INTEGER NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static SampleIndexRow[] ReadSampleIndex(
        SqliteConnection db, SqliteTransaction tx, string projectId)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT asset_id,sample_key,match_kind FROM sample_asset_index
            WHERE project_id=$project ORDER BY asset_id
            """;
        cmd.Parameters.AddWithValue("$project", projectId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<SampleIndexRow>();
        while (reader.Read())
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return rows.ToArray();
    }

    private static SampleIndexPlan PlanSampleIndex(
        SqliteConnection db, SqliteTransaction tx, string projectId)
    {
        using var query = db.CreateCommand();
        query.Transaction = tx;
        query.CommandText = """
            SELECT a.id,a.filename,a.stage,a.group_id,a.result_id
            FROM experiment_assets a
            JOIN experiment_results r ON r.id=a.result_id
            WHERE r.project_id=$project AND r.is_deleted=0 AND a.is_deleted=0
              AND a.stage IN ('raw','process','show','quant')
            ORDER BY a.id
            """;
        query.Parameters.AddWithValue("$project", projectId);
        var assets = new List<SampleIndexSource>();
        using (var reader = query.ExecuteReader())
            while (reader.Read())
                assets.Add(new(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4)));

        // Anchors are explicit RAW, SHOW and QUANT assets. PROCESS may have any
        // suffix; match only when separated by '_' or '-' to avoid CV01/CV010.
        var anchors = assets.Where(x => x.Stage is "raw" or "show" or "quant")
            .Select(x => DetectPrefix(x.FileName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var proposed = new List<SampleIndexRow>();
        var unresolved = 0;
        foreach (var asset in assets)
        {
            if (asset.Stage != "process")
            {
                proposed.Add(new(asset.AssetId, DetectPrefix(asset.FileName), "anchor"));
                continue;
            }
            var stem = Path.GetFileNameWithoutExtension(asset.FileName);
            var prefix = DetectPrefix(asset.FileName);
            var matches = anchors.Where(a =>
                prefix.Equals(a, StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith(a + "_", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith(a + "-", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                unresolved++;
                continue;
            }
            // Longest bounded match wins; an equal-length conflict is unresolved.
            if (matches.Length > 1 && matches[0].Length == matches[1].Length)
            {
                unresolved++;
                continue;
            }
            proposed.Add(new(asset.AssetId, matches[0], "inferred"));
        }
        var current = ReadSampleIndex(db, tx, projectId);
        var before = current.ToDictionary(x => x.AssetId, StringComparer.Ordinal);
        var after = proposed.ToDictionary(x => x.AssetId, StringComparer.Ordinal);
        var changes = new List<SampleIndexChange>();
        foreach (var id in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).Order())
        {
            before.TryGetValue(id, out var prior);
            after.TryGetValue(id, out var next);
            if (prior is not null && next is not null &&
                prior.MatchKind == next.MatchKind &&
                prior.SampleKey.Equals(next.SampleKey, StringComparison.OrdinalIgnoreCase))
                continue;
            var asset = assets.FirstOrDefault(x => x.AssetId == id);
            changes.Add(new(id, asset?.FileName ?? "(已移动或删除的素材)",
                prior?.SampleKey, next?.SampleKey));
        }

        // Includes original asset metadata and the previous index to reject
        // applying a preview after a concurrent import, move or rename.
        var canonical = string.Join("\n", assets.Select(x =>
                x.AssetId + "|" + x.ResultId + "|" + x.GroupId + "|" + x.FileName + "|" + x.Stage))
            + "\n--\n" + string.Join("\n", current.Select(x =>
                x.AssetId + "|" + x.SampleKey + "|" + x.MatchKind));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new(new(projectId, fingerprint, proposed.Count, unresolved, changes),
            current, proposed.OrderBy(x => x.AssetId, StringComparer.Ordinal).ToArray());
    }

    private static void WriteSampleIndex(SqliteConnection db, SqliteTransaction tx,
        string projectId, IReadOnlyList<SampleIndexRow> rows)
    {
        using (var remove = db.CreateCommand())
        {
            remove.Transaction = tx;
            remove.CommandText = "DELETE FROM sample_asset_index WHERE project_id=$project";
            remove.Parameters.AddWithValue("$project", projectId);
            remove.ExecuteNonQuery();
        }
        foreach (var row in rows)
        {
            using var add = db.CreateCommand();
            add.Transaction = tx;
            add.CommandText = """
                INSERT INTO sample_asset_index(asset_id,project_id,sample_key,match_kind)
                VALUES($asset,$project,$key,$kind)
                """;
            add.Parameters.AddWithValue("$asset", row.AssetId);
            add.Parameters.AddWithValue("$project", projectId);
            add.Parameters.AddWithValue("$key", row.SampleKey);
            add.Parameters.AddWithValue("$kind", row.MatchKind);
            add.ExecuteNonQuery();
        }
        // Keep the derived labels in the same transaction as index changes,
        // including explicit rebuild/undo and imports. No archive edits.
        RefreshSystemTags(db,tx,projectId);
    }

    /// <summary>Read-only preview, including unmatched PROCESS assets.</summary>
    public SampleIndexPreview PreviewSampleIndexRebuild(string projectId)
    {
        using var db = Connect();
        using var tx = db.BeginTransaction();
        var preview = PlanSampleIndex(db, tx, projectId).Preview;
        tx.Commit();
        return preview;
    }

    /// <summary>
    /// Explicit rebuild for legacy data, with a stale-preview guard and
    /// a transactionally stored undo snapshot of index rows only.
    /// </summary>
    public SampleIndexPreview ApplySampleIndexRebuild(string projectId, string expectedFingerprint)
    {
        using var db = Connect();
        using var tx = db.BeginTransaction();
        var plan = PlanSampleIndex(db, tx, projectId);
        if (!string.Equals(plan.Preview.Fingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("实验数据已变化，请重新预览样本索引。");
        if (plan.Preview.Changes.Count > 0)
        {
            WriteSampleIndex(db, tx, projectId, plan.Proposed);
            using var snapshot = db.CreateCommand();
            snapshot.Transaction = tx;
            snapshot.CommandText = """
                INSERT INTO sample_index_rebuild_undo(
                    project_id,previous_json,applied_json,created_at)
                VALUES($project,$previous,$applied,$now)
                """;
            snapshot.Parameters.AddWithValue("$project", projectId);
            snapshot.Parameters.AddWithValue("$previous", JsonSerializer.Serialize(plan.Current));
            snapshot.Parameters.AddWithValue("$applied", JsonSerializer.Serialize(plan.Proposed));
            snapshot.Parameters.AddWithValue("$now", UtcNow());
            snapshot.ExecuteNonQuery();
        }
        tx.Commit();
        return plan.Preview;
    }

    /// <summary>
    /// Undo only if the index still matches the applied snapshot. Never
    /// rollback newer imports, moves or edits by accident.
    /// </summary>
    public bool UndoLastSampleIndexRebuild(string projectId)
    {
        using var db = Connect();
        using var tx = db.BeginTransaction();
        long seq;
        SampleIndexRow[] previous, applied;
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT seq,previous_json,applied_json
                FROM sample_index_rebuild_undo
                WHERE project_id=$project AND undone=0 ORDER BY seq DESC LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$project", projectId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return false;
            seq = reader.GetInt64(0);
            previous = JsonSerializer.Deserialize<SampleIndexRow[]>(reader.GetString(1)) ?? [];
            applied = JsonSerializer.Deserialize<SampleIndexRow[]>(reader.GetString(2)) ?? [];
        }
        var current = ReadSampleIndex(db, tx, projectId);
        if (!current.SequenceEqual(applied))
            throw new InvalidOperationException("索引在重建后已有新变化，不能直接撤销旧快照。");
        WriteSampleIndex(db, tx, projectId, previous);
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE sample_index_rebuild_undo SET undone=1 WHERE seq=$seq";
            cmd.Parameters.AddWithValue("$seq", seq);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    // Called within a write transaction for new imports/changes. Do not log
    // automatic maintenance as a user-confirmed reconstruction.
    private static void RefreshSampleIndex(
        SqliteConnection db, SqliteTransaction tx, string projectId)
    {
        var plan = PlanSampleIndex(db, tx, projectId);
        if (plan.Preview.Changes.Count > 0)
            WriteSampleIndex(db, tx, projectId, plan.Proposed);
        else
            // CSV-only or unresolved imports have no sample-index delta,
            // but every imported asset still needs its one-time tag snapshot.
            RefreshSystemTags(db,tx,projectId);
    }

    private static void RefreshSampleIndexForResults(
        SqliteConnection db, SqliteTransaction tx, IEnumerable<string> resultIds)
    {
        var projects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resultId in resultIds.Distinct(StringComparer.Ordinal))
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT project_id FROM experiment_results WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", resultId);
            if (cmd.ExecuteScalar() is string projectId) projects.Add(projectId);
        }
        foreach (var projectId in projects)
            RefreshSampleIndex(db, tx, projectId);
    }

    private static void RefreshSampleIndexForAssets(
        SqliteConnection db, SqliteTransaction tx, IEnumerable<string> assetIds)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assetId in assetIds.Distinct(StringComparer.Ordinal))
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT result_id FROM experiment_assets WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", assetId);
            if (cmd.ExecuteScalar() is string resultId) results.Add(resultId);
        }
        RefreshSampleIndexForResults(db, tx, results);
    }

    /// <summary>
    /// Related assets are resolved by stable D2 Sample Unit identity first.
    /// The legacy index is only used when the selected asset has no stable
    /// membership; fallback never crosses two known experiment names.
    /// The CTE uses UNION (not UNION ALL) to yield one row per Asset ID.
    /// Same SHA-256 under distinct Asset IDs is retained for the UI to group.
    /// </summary>
    public IReadOnlyList<ExperimentAsset> ListSameSampleAssets(string assetId)
    {
        using var db=Connect();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            WITH candidate_ids(id) AS (
                SELECT peers.asset_id
                FROM sample_unit_assets mine
                JOIN sample_unit_assets peers
                  ON peers.sample_unit_id=mine.sample_unit_id
                WHERE mine.asset_id=$id
                UNION
                SELECT peers.asset_id
                FROM sample_asset_index mine
                JOIN sample_asset_index peers
                  ON peers.project_id=mine.project_id
                 AND peers.sample_key=mine.sample_key
                LEFT JOIN sample_unit_assets stable ON stable.asset_id=mine.asset_id
                LEFT JOIN asset_system_tags mine_tags ON mine_tags.asset_id=mine.asset_id
                LEFT JOIN asset_system_tags peer_tags ON peer_tags.asset_id=peers.asset_id
                WHERE mine.asset_id=$id AND stable.asset_id IS NULL
                  AND (mine_tags.asset_id IS NULL OR peer_tags.asset_id IS NULL
                    OR mine_tags.experiment_name=peer_tags.experiment_name COLLATE NOCASE)
            )
            SELECT DISTINCT a.id,a.result_id,a.group_id,g.prefix,a.stage,
                   a.relative_path,a.filename,a.size,a.sha256
            FROM candidate_ids peers
            JOIN experiment_assets a ON a.id=peers.id
            JOIN experiment_groups g ON g.id=a.group_id
            JOIN experiment_results r ON r.id=a.result_id
            JOIN experiment_projects p ON p.id=r.project_id
            WHERE a.is_deleted=0 AND r.is_deleted=0 AND p.is_deleted=0
              AND a.id<>$id
            ORDER BY CASE a.stage
                WHEN 'raw' THEN 0 WHEN 'process' THEN 1
                WHEN 'show' THEN 2 WHEN 'quant' THEN 3 ELSE 4 END,
                a.filename COLLATE NOCASE,a.id
            """;
        cmd.Parameters.AddWithValue("$id",assetId);
        using var rd=cmd.ExecuteReader();
        var result=new List<ExperimentAsset>();
        while(rd.Read())
            result.Add(new(rd.GetString(0),rd.GetString(1),rd.GetString(2),
                rd.GetString(3),rd.GetString(4),rd.GetString(5),rd.GetString(6),
                rd.GetInt64(7),rd.GetString(8)));
        return result;
    }
}
