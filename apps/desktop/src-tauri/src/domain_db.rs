//! Schema-only foundation for Data-M's future repository.
//! No Tauri IPC is exposed here; no user asset paths or files are touched.
use rusqlite::{Connection, OptionalExtension, Result};

const MIGRATION_1: &str = include_str!("../migrations/0001_initial.sql");
pub const SCHEMA_VERSION: i64 = 1;

pub fn initialize_empty_database(connection: &mut Connection) -> Result<()> {
    connection.pragma_update(None, "foreign_keys", "ON")?;
    // Refuse to overwrite another application's or a future Data-M database.
    let version: i64 = connection.pragma_query_value(None, "user_version", |row| row.get(0))?;
    if version > SCHEMA_VERSION {
        return Err(rusqlite::Error::InvalidQuery);
    }
    let known: Option<String> = connection
        .query_row(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_migrations'",
            [],
            |row| row.get(0),
        )
        .optional()?;
    if known.is_none() {
        // Existing unrecognized tables are not ours; never modify them.
        let non_system: i64 = connection.query_row(
            "SELECT count(*) FROM sqlite_master WHERE type IN ('table','view','trigger','index')              AND name NOT LIKE 'sqlite_%'",
            [],
            |row| row.get(0),
        )?;
        if non_system != 0 || version != 0 {
            return Err(rusqlite::Error::InvalidQuery);
        }
        let transaction = connection.transaction()?;
        transaction.execute_batch(MIGRATION_1)?;
        transaction.pragma_update(None, "user_version", SCHEMA_VERSION)?;
        transaction.commit()?;
    } else {
        // Fail closed on unknown or partially modified schema history.
        let migration: Option<i64> = connection
            .query_row(
                "SELECT max(version) FROM schema_migrations",
                [],
                |row| row.get(0),
            )?;
        if migration != Some(SCHEMA_VERSION) || version != SCHEMA_VERSION {
            return Err(rusqlite::Error::InvalidQuery);
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use rusqlite::{params, Connection};

    fn db() -> Connection {
        let mut connection = Connection::open_in_memory().unwrap();
        initialize_empty_database(&mut connection).unwrap();
        connection
    }
    fn base(connection: &Connection) {
        connection.execute("INSERT INTO projects(id,name) VALUES('p','Project')", []).unwrap();
        connection.execute("INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme')", []).unwrap();
        connection.execute("INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay')", []).unwrap();
        connection.execute("INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Control')", []).unwrap();
        connection.execute("INSERT INTO replicates(id,group_id,label) VALUES('r','g','Replicate 1')", []).unwrap();
    }
    fn asset(connection: &Connection) {
        connection.execute("INSERT INTO blobs(sha256,size_bytes) VALUES(?1,4)", ["a".repeat(64)]).unwrap();
        connection.execute("INSERT INTO data_assets(id,name) VALUES('asset1','file.tif')", []).unwrap();
        connection.execute(
            "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)              VALUES('v1','asset1',1,?1,'original.tif')",
            ["a".repeat(64)],
        ).unwrap();
    }
    #[test]
    fn migration_idempotence_and_foreign_keys() {
        let mut connection=db();
        initialize_empty_database(&mut connection).unwrap();
        let version:i64=connection.pragma_query_value(None,"user_version",|row|row.get(0)).unwrap();
        assert_eq!(version,1);
        let enabled:i64=connection.pragma_query_value(None,"foreign_keys",|row|row.get(0)).unwrap();
        assert_eq!(enabled,1);
        let count:i64=connection.query_row("SELECT count(*) FROM schema_migrations",[],|r|r.get(0)).unwrap();
        assert_eq!(count,1);
    }
    #[test]
    fn foreign_keys_and_sample_count_survive_asset_links() {
        let connection=db();base(&connection);asset(&connection);
        assert!(connection.execute("INSERT INTO replicates(id,group_id,label) VALUES('orphan','missing','x')",[]).is_err());
        connection.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)              VALUES('l1','asset1','a','g','r','raw','file.tif')",[],
        ).unwrap();
        connection.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)              VALUES('l2','asset1','a','g','r','processed','file_processed.tif')",[],
        ).unwrap();
        let sample_count:i64=connection.query_row("SELECT count(*) FROM replicates",[],|r|r.get(0)).unwrap();
        assert_eq!(sample_count,1);
        assert!(connection.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)              VALUES('bad','asset1','a','g','missing','raw','bad.tif')",[],
        ).is_err());
    }
    #[test]
    fn refuses_cross_assay_and_cross_group_links() {
        let connection=db();base(&connection);asset(&connection);
        connection.execute("INSERT INTO assays(id,theme_id,name) VALUES('a2','t','Another')",[]).unwrap();
        connection.execute("INSERT INTO experiment_groups(id,assay_id,name) VALUES('g2','a2','Other')",[]).unwrap();
        assert!(connection.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)              VALUES('bad','asset1','a2','g','r','raw','f.tif')",[]
        ).is_err());
        assert!(connection.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)              VALUES('bad2','asset1','a2','g2','r','raw','f.tif')",[]
        ).is_err());
    }
    #[test]
    fn case_insensitive_active_filenames_and_soft_delete() {
        let connection=db();base(&connection);asset(&connection);
        connection.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,stage,display_name) VALUES('one','asset1','a','g','raw','CASE.tif')",[]).unwrap();
        assert!(connection.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,stage,display_name) VALUES('two','asset1','a','g','raw','case.TIF')",[]).is_err());
        connection.execute("UPDATE asset_links SET deleted_at='2026-11-15T00:00:00Z' WHERE id='one'",[]).unwrap();
        connection.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,stage,display_name) VALUES('two','asset1','a','g','raw','case.TIF')",[]).unwrap();
    }
    #[test]
    fn immutable_blob_and_versions_and_valid_digest() {
        let connection=db();base(&connection);asset(&connection);
        assert!(connection.execute("UPDATE blobs SET size_bytes=0",[]).is_err());
        assert!(connection.execute("DELETE FROM blobs",[]).is_err());
        assert!(connection.execute("UPDATE asset_versions SET source_name='changed'",[]).is_err());
        assert!(connection.execute("DELETE FROM asset_versions",[]).is_err());
        assert!(connection.execute("INSERT INTO blobs(sha256,size_bytes) VALUES('invalid',4)",[]).is_err());
    }
    #[test]
    fn panel_links_pin_versions_and_do_not_follow_mutable_paths() {
        let connection=db();base(&connection);asset(&connection);
        connection.execute("INSERT INTO papers(id,project_id,title) VALUES('paper','p','Study')",[]).unwrap();
        connection.execute("INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','Figure 1')",[]).unwrap();
        connection.execute("INSERT INTO panels(id,figure_id,label) VALUES('panel','fig','A')",[]).unwrap();
        connection.execute("INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage) VALUES('panel','v1','display')",[]).unwrap();
        connection.execute("UPDATE data_assets SET name='renamed.tif' WHERE id='asset1'",[]).unwrap();
        let count:i64=connection.query_row(
            "SELECT count(*) FROM panel_asset_versions WHERE asset_version_id='v1'",[],
            |row|row.get(0),
        ).unwrap();
        assert_eq!(count,1);
        assert!(connection.execute("DELETE FROM data_assets WHERE id='asset1'",[]).is_err());
    }
    #[test]
    fn inclusion_and_representative_are_relationships() {
        let connection=db();base(&connection);asset(&connection);
        connection.execute("INSERT INTO statistical_analyses(id,assay_id,name) VALUES('st','a','Analysis')",[]).unwrap();
        connection.execute("INSERT INTO analysis_samples(analysis_id,replicate_id,included) VALUES('st','r',1)",[]).unwrap();
        assert!(connection.execute("UPDATE analysis_samples SET included=2",[]).is_err());
        connection.execute("INSERT INTO papers(id,project_id,title) VALUES('paper','p','Study')",[]).unwrap();
        connection.execute("INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','Fig')",[]).unwrap();
        connection.execute("INSERT INTO panels(id,figure_id,label) VALUES('panel','fig','A')",[]).unwrap();
        connection.execute(
            "INSERT INTO representative_sources(panel_id,replicate_id,asset_version_id) VALUES('panel','r','v1')",[],
        ).unwrap();
    }
    #[test]
    fn refuses_foreign_or_newer_database_without_side_effects() {
        let mut connection=Connection::open_in_memory().unwrap();
        connection.execute_batch("CREATE TABLE other_application(id TEXT);").unwrap();
        assert!(initialize_empty_database(&mut connection).is_err());
        let count:i64=connection.query_row("SELECT count(*) FROM sqlite_master WHERE name='schema_migrations'",[],|r|r.get(0)).unwrap();
        assert_eq!(count,0);
        let mut future=Connection::open_in_memory().unwrap();
        future.pragma_update(None,"user_version",2).unwrap();
        assert!(initialize_empty_database(&mut future).is_err());
    }
}
