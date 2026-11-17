use super::*;
use crate::project_tree::with_database_at;
use rusqlite::{params, Connection};
use std::fs;
use std::path::PathBuf;
use tempfile::TempDir;

struct Fixture {
    _dir: TempDir,
    database: PathBuf,
    backup_root: PathBuf,
    restore_root: PathBuf,
    object: PathBuf,
    sha: String,
    source: PathBuf,
}
fn fixture() -> Fixture {
    let dir = tempfile::tempdir().unwrap();
    let live = dir.path().join("live");
    fs::create_dir(&live).unwrap();
    let database = live.join("project-tree.sqlite");
    let cas = live.join("cas").join("objects");
    fs::create_dir_all(&cas).unwrap();
    let source = dir.path().join("immutable-original.bin");
    fs::write(&source, b"S7 scientific fixture bytes, unchanged").unwrap();
    let sha = format!("{:x}", Sha256::digest(fs::read(&source).unwrap()));
    let object = cas.join(&sha);
    fs::copy(&source, &object).unwrap();
    with_database_at(&database, |c| {
        c.execute_batch(
            "INSERT INTO projects(id,name) VALUES('p','Project');
            INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme');
            INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay');
            INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Group');
            INSERT INTO replicates(id,group_id,label) VALUES('r','g','R1');
            INSERT INTO papers(id,project_id,title) VALUES('paper','p','Research');
            INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','F1');
            INSERT INTO panels(id,figure_id,label) VALUES('panel','fig','A');",
        )?;
        c.execute(
            "INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",
            params![sha, fs::metadata(&source).unwrap().len() as i64],
        )?;
        c.execute(
            "INSERT INTO data_assets(id,name) VALUES('asset','original.bin')",
            [],
        )?;
        c.execute(
            "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
            VALUES('version','asset',1,?1,'original.bin')",
            [&sha],
        )?;
        c.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
            VALUES('link','asset','a','g','r','raw','original.bin')",
            [],
        )?;
        c.execute(
            "INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
            VALUES('panel','version','display')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    Fixture {
        database,
        backup_root: dir.path().join("backups"),
        restore_root: dir.path().join("restored"),
        object,
        sha,
        source,
        _dir: dir,
    }
}
fn nop(_: SafetyProgress) {}
fn req(
    action: &str,
    id: Option<&str>,
    label: Option<&str>,
    confirm: Option<bool>,
) -> SafetyRequest {
    SafetyRequest {
        action: action.into(),
        snapshot_id: id.map(str::to_owned),
        restore_label: label.map(str::to_owned),
        confirm,
    }
}
fn execute(f: &Fixture, request: SafetyRequest) -> Result<SafetyResponse, TreeError> {
    execute_at(&f.database, &f.backup_root, &f.restore_root, request, &nop)
}
fn backup(f: &Fixture) -> String {
    execute(f, req("backup", None, None, None))
        .unwrap()
        .snapshot_id
        .unwrap()
}
fn snap(f: &Fixture, id: &str) -> PathBuf {
    f.backup_root.join(id)
}
#[test]
fn s7_audit_clean_snapshot_manifest_and_listing_preserve_file_bytes() {
    let f = fixture();
    let before = fs::read(&f.source).unwrap();
    let audit = execute(&f, req("audit", None, None, None)).unwrap();
    assert_eq!(audit.status, "clean");
    let report = audit.report.unwrap();
    assert_eq!(report.checked_blobs, 1);
    assert_eq!(report.verified_blobs, 1);
    assert_eq!(report.table_counts["panel_asset_versions"], 1);
    assert_eq!(report.table_counts["replicates"], 1);
    let id = backup(&f);
    assert_eq!(id.len(), 32);
    let manifest: Manifest =
        serde_json::from_slice(&fs::read(snap(&f, &id).join("manifest.json")).unwrap()).unwrap();
    assert_eq!(manifest.format_version, 1);
    assert_eq!(manifest.schema_version, 3);
    assert_eq!(manifest.table_counts["analysis_asset_versions"], 0);
    assert_eq!(manifest.blobs.len(), 1);
    assert_eq!(manifest.versions[0].id, "version");
    assert_eq!(manifest.table_counts["asset_links"], 1);
    assert_eq!(manifest.blobs[0].sha256, f.sha);
    let listing = execute(&f, req("list", None, None, None)).unwrap();
    assert_eq!(listing.snapshots.len(), 1);
    assert_eq!(listing.snapshots[0].snapshot_id, id);
    assert_eq!(
        execute(&f, req("verify", Some(&id), None, None))
            .unwrap()
            .status,
        "verified"
    );
    assert_eq!(fs::read(&f.source).unwrap(), before);
}
#[test]
fn s7_backup_restore_after_disposable_source_removed_matches_every_link_version_and_blob() {
    let f = fixture();
    let source_before = fs::read(&f.source).unwrap();
    let id = backup(&f);
    fs::remove_dir_all(f.database.parent().unwrap()).unwrap();
    let response = execute(
        &f,
        req("restore", Some(&id), Some("fresh-repo"), Some(true)),
    )
    .unwrap();
    assert_eq!(response.status, "restored");
    let restored = f.restore_root.join("fresh-repo");
    let db = open_recognized(&restored.join("project-tree.sqlite")).unwrap();
    assert_eq!(counts(&db).unwrap()["replicates"], 1);
    assert_eq!(counts(&db).unwrap()["asset_links"], 1);
    assert_eq!(counts(&db).unwrap()["panel_asset_versions"], 1);
    assert_eq!(version_list(&db).unwrap()[0].id, "version");
    assert_eq!(blob_list(&db).unwrap()[0].sha256, f.sha);
    assert_eq!(
        fs::read(restored.join("cas").join("objects").join(&f.sha)).unwrap(),
        source_before
    );
    assert_eq!(fs::read(&f.source).unwrap(), source_before);
}
#[test]
fn s7_detects_same_size_tampered_blob_and_refuses_backup_without_repair() {
    let f = fixture();
    let original = fs::read(&f.object).unwrap();
    fs::write(&f.object, vec![b'X'; original.len()]).unwrap();
    let report = execute(&f, req("audit", None, None, None))
        .unwrap()
        .report
        .unwrap();
    assert!(report.issues.iter().any(|x| x.code == "BLOB_HASH"));
    assert_eq!(
        execute(&f, req("backup", None, None, None))
            .unwrap_err()
            .code,
        "INTEGRITY_FAILED"
    );
    assert_eq!(fs::read(&f.object).unwrap(), vec![b'X'; original.len()]);
    assert!(!f.backup_root.exists());
}
#[test]
fn s7_missing_blob_is_reported_without_modifying_the_database() {
    let f = fixture();
    fs::remove_file(&f.object).unwrap();
    let report = execute(&f, req("audit", None, None, None))
        .unwrap()
        .report
        .unwrap();
    assert!(report.issues.iter().any(|x| x.code == "MISSING_BLOB"));
    assert_eq!(report.verified_blobs, 0);
    let db = open_recognized(&f.database).unwrap();
    assert_eq!(counts(&db).unwrap()["blobs"], 1);
}
#[test]
fn s7_orphan_object_and_unknown_staging_detected_without_deletion() {
    let f = fixture();
    let orphan = f.object.parent().unwrap().join("unexpected-object");
    fs::write(&orphan, b"orphan").unwrap();
    let stage = f.database.parent().unwrap().join("cas").join("staging");
    fs::create_dir_all(&stage).unwrap();
    fs::write(stage.join("untracked-stage"), b"unfinished").unwrap();
    let report = execute(&f, req("audit", None, None, None))
        .unwrap()
        .report
        .unwrap();
    assert!(report.issues.iter().any(|x| x.code == "ORPHAN_OBJECT"));
    assert!(report.issues.iter().any(|x| x.code == "ORPHAN_STAGING"));
    assert_eq!(fs::read(orphan).unwrap(), b"orphan");
    assert_eq!(
        fs::read(stage.join("untracked-stage")).unwrap(),
        b"unfinished"
    );
    assert_eq!(
        execute(&f, req("backup", None, None, None))
            .unwrap_err()
            .code,
        "INTEGRITY_FAILED"
    );
}
#[test]
fn s7_foreign_key_violation_is_reported_and_unmodified() {
    let f = fixture();
    let db = Connection::open(&f.database).unwrap();
    db.pragma_update(None, "foreign_keys", "OFF").unwrap();
    db.execute(
        "INSERT INTO themes(id,project_id,name) VALUES('broken','missing','Broken')",
        [],
    )
    .unwrap();
    db.close().unwrap();
    let report = execute(&f, req("audit", None, None, None))
        .unwrap()
        .report
        .unwrap();
    assert!(report.issues.iter().any(|x| x.code == "FOREIGN_KEY"));
    assert_eq!(
        execute(&f, req("backup", None, None, None))
            .unwrap_err()
            .code,
        "INTEGRITY_FAILED"
    );
}
#[test]
fn s7_tampered_sealed_blob_prevents_verify_or_restore_and_preserves_current_source() {
    let f = fixture();
    let id = backup(&f);
    let changed = snap(&f, &id).join("objects").join(&f.sha);
    let bytes = fs::read(&changed).unwrap();
    fs::write(&changed, vec![b'Y'; bytes.len()]).unwrap();
    assert_eq!(
        execute(&f, req("verify", Some(&id), None, None))
            .unwrap_err()
            .code,
        "SNAPSHOT_CORRUPT"
    );
    assert_eq!(
        execute(
            &f,
            req("restore", Some(&id), Some("bad-restore"), Some(true))
        )
        .unwrap_err()
        .code,
        "SNAPSHOT_CORRUPT"
    );
    assert!(!f.restore_root.exists());
    assert_eq!(fs::read(&f.object).unwrap(), fs::read(&f.source).unwrap());
}
#[test]
fn s7_invalid_manifest_and_database_mutation_are_rejected_before_restore() {
    let f = fixture();
    let id = backup(&f);
    let path = snap(&f, &id).join("manifest.json");
    let before = fs::read(&path).unwrap();
    fs::write(&path, b"{\"formatVersion\":99}").unwrap();
    assert_eq!(
        execute(&f, req("verify", Some(&id), None, None))
            .unwrap_err()
            .code,
        "SNAPSHOT_CORRUPT"
    );
    fs::write(&path, before).unwrap();
    let db = snap(&f, &id).join("database.sqlite");
    let mut bytes = fs::read(&db).unwrap();
    let last = bytes.len() - 1;
    bytes[last] ^= 0x01;
    fs::write(&db, bytes).unwrap();
    assert_eq!(
        execute(&f, req("verify", Some(&id), None, None))
            .unwrap_err()
            .code,
        "SNAPSHOT_CORRUPT"
    );
    assert_eq!(
        fs::read(&f.source).unwrap(),
        b"S7 scientific fixture bytes, unchanged"
    );
}
#[test]
fn s7_injected_after_snapshot_blob_copy_and_before_publish_fail_closed_then_retry() {
    let f = fixture();
    for phase in ["after_sqlite_snapshot", "after_blob_copy", "before_publish"] {
        let result = snapshot::backup_with_hook(&f.database, &f.backup_root, &nop, &|p| {
            if p == phase {
                Err(failure("DISK_WRITE_FAILED"))
            } else {
                Ok(())
            }
        });
        assert_eq!(result.unwrap_err().code, "DISK_WRITE_FAILED");
    }
    assert_eq!(
        execute(&f, req("list", None, None, None))
            .unwrap()
            .snapshots
            .len(),
        0
    );
    assert_eq!(fs::read(&f.source).unwrap(), fs::read(&f.object).unwrap());
    let id = backup(&f);
    assert!(snap(&f, &id).is_dir());
}
#[test]
fn s7_restore_failure_before_publish_does_not_create_target_or_damage_backup() {
    let f = fixture();
    let id = backup(&f);
    for phase in [
        "after_restore_sqlite",
        "after_restore_blob",
        "before_restore_publish",
    ] {
        let result = snapshot::restore_with_hook(
            &f.backup_root,
            &f.restore_root,
            &id,
            &format!("test-{phase}"),
            &nop,
            &|p| {
                if p == phase {
                    Err(failure("DISK_WRITE_FAILED"))
                } else {
                    Ok(())
                }
            },
        );
        assert_eq!(result.unwrap_err().code, "DISK_WRITE_FAILED");
        assert!(!f.restore_root.join(format!("test-{phase}")).exists());
    }
    assert_eq!(
        execute(&f, req("verify", Some(&id), None, None))
            .unwrap()
            .status,
        "verified"
    );
    execute(&f, req("restore", Some(&id), Some("retry-ok"), Some(true))).unwrap();
}
#[test]
fn s7_second_restore_never_overwrites_existing_directory_or_data() {
    let f = fixture();
    let id = backup(&f);
    execute(&f, req("restore", Some(&id), Some("same"), Some(true))).unwrap();
    let restored = f
        .restore_root
        .join("same")
        .join("cas")
        .join("objects")
        .join(&f.sha);
    let original = fs::read(&restored).unwrap();
    assert_eq!(
        execute(&f, req("restore", Some(&id), Some("same"), Some(true)))
            .unwrap_err()
            .code,
        "RESTORE_CONFLICT"
    );
    assert_eq!(fs::read(restored).unwrap(), original);
}
#[test]
fn s7_unknown_database_is_not_initialized_or_modified_by_audit_and_backup() {
    let f = fixture();
    let other = f.database.parent().unwrap().join("unknown.sqlite");
    fs::write(&other, b"NOT A DATA-M DATABASE").unwrap();
    let before = fs::read(&other).unwrap();
    assert_eq!(
        execute_at(
            &other,
            &f.backup_root,
            &f.restore_root,
            req("audit", None, None, None),
            &nop
        )
        .unwrap_err()
        .code,
        "UNKNOWN_DATABASE"
    );
    assert_eq!(
        execute_at(
            &other,
            &f.backup_root,
            &f.restore_root,
            req("backup", None, None, None),
            &nop
        )
        .unwrap_err()
        .code,
        "UNKNOWN_DATABASE"
    );
    assert_eq!(fs::read(&other).unwrap(), before);
}
#[test]
fn s7_wal_committed_pages_are_included_and_uncommitted_writer_is_excluded() {
    let f = fixture();
    let writer = Connection::open(&f.database).unwrap();
    writer.pragma_update(None, "journal_mode", "WAL").unwrap();
    writer
        .execute(
            "INSERT INTO projects(id,name) VALUES('wal','Committed in WAL')",
            [],
        )
        .unwrap();
    writer
        .execute_batch(
            "BEGIN IMMEDIATE; INSERT INTO projects(id,name) VALUES('uncommitted','Not saved');",
        )
        .unwrap();
    let id = backup(&f);
    let snapdb = open_recognized(&snap(&f, &id).join("database.sqlite")).unwrap();
    let committed: i64 = snapdb
        .query_row("SELECT count(*) FROM projects WHERE id='wal'", [], |r| {
            r.get(0)
        })
        .unwrap();
    let absent: i64 = snapdb
        .query_row(
            "SELECT count(*) FROM projects WHERE id='uncommitted'",
            [],
            |r| r.get(0),
        )
        .unwrap();
    assert_eq!(committed, 1);
    assert_eq!(absent, 0);
    writer.execute_batch("ROLLBACK").unwrap();
}
#[test]
fn s7_refuses_incomplete_import_jobs_and_does_not_create_snapshot() {
    let f = fixture();
    with_database_at(&f.database, |c| {
        c.execute(
            "INSERT INTO import_jobs(id,request_id,payload,target_json,status)
         VALUES('job','req','{}','{}','running')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute(&f, req("backup", None, None, None))
            .unwrap_err()
            .code,
        "BACKUP_BUSY"
    );
    assert!(!f.backup_root.exists());
}
#[test]
fn s7_rejects_unconfirmed_restore_paths_and_reparse_like_labels() {
    let f = fixture();
    let id = backup(&f);
    for label in ["..", "../other", "con", "COM1", "LPT3", "a:b", "a\\b", ""] {
        assert_eq!(
            execute(&f, req("restore", Some(&id), Some(label), Some(true)))
                .unwrap_err()
                .code,
            "INVALID_REQUEST"
        );
    }
    assert_eq!(
        execute(&f, req("restore", Some(&id), Some("safe"), None))
            .unwrap_err()
            .code,
        "INVALID_REQUEST"
    );
    assert!(!f.restore_root.exists());
}
