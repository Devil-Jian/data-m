using Microsoft.Data.Sqlite;

namespace PaperArchive.Core.Workspace;

public sealed partial class ExperimentWorkspace
{
    /// <summary>
    /// V1.6-B: quantification-only images are not finished SHOW materials.
    /// SQLite does not support altering CHECK(stage IN (...)); rebuild the
    /// assets table atomically, preserving every original asset ID and FK.
    /// </summary>
    private void InitializeQuantImageStage()
    {
        using var db=Connect();
        using(var check=db.CreateCommand())
        {
            check.CommandText="SELECT sql FROM sqlite_master WHERE type='table' AND name='experiment_assets'";
            var sql=check.ExecuteScalar() as string
                ??throw new InvalidDataException("实验图片数据表不存在。");
            if(sql.Contains("'quant'",StringComparison.OrdinalIgnoreCase))
                return;
        }

        // Foreign key checks are re-enabled only after the table replacement.
        // Never drop/recreate another table, mutate existing IDs, or change blobs.
        using(var disable=db.CreateCommand())
        {
            disable.CommandText="PRAGMA foreign_keys=OFF";
            disable.ExecuteNonQuery();
        }
        try
        {
            using var tx=db.BeginTransaction();
            using(var command=db.CreateCommand())
            {
                command.Transaction=tx;
                command.CommandText="""
                    CREATE TABLE experiment_assets_quant_upgrade (
                      id TEXT PRIMARY KEY,
                      result_id TEXT NOT NULL REFERENCES experiment_results(id),
                      group_id TEXT NOT NULL REFERENCES experiment_groups(id),
                      stage TEXT NOT NULL CHECK(stage IN (
                        'raw','process','show','quant','statistics','other')),
                      relative_path TEXT NOT NULL,
                      filename TEXT NOT NULL,
                      size INTEGER NOT NULL,
                      sha256 TEXT NOT NULL,
                      imported_at TEXT NOT NULL,
                      is_deleted INTEGER NOT NULL DEFAULT 0,
                      UNIQUE(result_id,relative_path)
                    );
                    INSERT INTO experiment_assets_quant_upgrade(
                      id,result_id,group_id,stage,relative_path,filename,
                      size,sha256,imported_at,is_deleted)
                    SELECT id,result_id,group_id,stage,relative_path,filename,
                      size,sha256,imported_at,is_deleted
                    FROM experiment_assets;
                    DROP TABLE experiment_assets;
                    ALTER TABLE experiment_assets_quant_upgrade RENAME TO experiment_assets;
                    CREATE INDEX ix_experiment_assets_group
                      ON experiment_assets(group_id);
                    """;
                command.ExecuteNonQuery();
            }
            using(var verify=db.CreateCommand())
            {
                verify.Transaction=tx;
                verify.CommandText="PRAGMA foreign_key_check";
                using var reader=verify.ExecuteReader();
                if(reader.Read())
                    throw new InvalidDataException(
                        "数据分类升级后发现外键不一致，已取消升级。");
            }
            tx.Commit();
        }
        finally
        {
            using var enable=db.CreateCommand();
            enable.CommandText="PRAGMA foreign_keys=ON";
            enable.ExecuteNonQuery();
        }
    }
}
