use super::*;
use crate::project_tree::with_database_at;
use sha2::{Digest, Sha256};
use std::fs;
use std::path::PathBuf;
use tempfile::TempDir;

fn fixture() -> (TempDir, PathBuf) {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("s5.sqlite");
    with_database_at(&path, |c| {
        c.execute("INSERT INTO projects(id,name) VALUES('p','Project')", [])?;
        c.execute(
            "INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme')",
            [],
        )?;
        c.execute(
            "INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay')",
            [],
        )?;
        c.execute(
            "INSERT INTO experiment_groups(id,assay_id,name) VALUES('g1','a','Control')",
            [],
        )?;
        c.execute(
            "INSERT INTO experiment_groups(id,assay_id,name) VALUES('g2','a','Treatment')",
            [],
        )?;
        c.execute(
            "INSERT INTO replicates(id,group_id,label) VALUES('r1','g1','R1')",
            [],
        )?;
        c.execute(
            "INSERT INTO replicates(id,group_id,label) VALUES('r2','g2','R2')",
            [],
        )?;
        c.execute(
            "INSERT INTO replicates(id,group_id,label) VALUES('r3','g1','R3')",
            [],
        )?;
        for n in 0..10 {
            let bytes = format!("S5-immutable-test-blob-{n}").into_bytes();
            fs::write(dir.path().join(format!("blob-{n}")), &bytes)
                .map_err(|_| failure("DATABASE_UNAVAILABLE"))?;
            let hash = format!("{:x}", Sha256::digest(&bytes));
            let asset_id = format!("asset{n}");
            let version_id = format!("version{n}");
            let link_id = format!("link{n}");
            let name = format!("raw{n:02}.png");
            c.execute(
                "INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",
                params![hash, bytes.len() as i64],
            )?;
            c.execute(
                "INSERT INTO data_assets(id,name) VALUES(?1,?2)",
                params![asset_id, name],
            )?;
            c.execute(
                "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
                 VALUES(?1,?2,1,?3,?4)",
                params![version_id, asset_id, hash, name],
            )?;
            c.execute(
                "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
                 VALUES(?1,?2,'a','g1','r1','raw',?3)",
                params![link_id, asset_id, name],
            )?;
        }
        Ok(())
    })
    .unwrap();
    (dir, path)
}

fn scope(assay: &str, stage: &str, group: Option<&str>, replicate: Option<&str>) -> AssetScope {
    AssetScope {
        assay_id: assay.into(),
        stage: stage.into(),
        group_id: group.map(str::to_string),
        replicate_id: replicate.map(str::to_string),
    }
}
fn request(action: &str, token: &str, indices: &[usize], target: AssetScope) -> TransferRequest {
    TransferRequest {
        action: action.into(),
        request_id: token.into(),
        items: indices
            .iter()
            .map(|n| TransferSource {
                asset_link_id: format!("link{n}"),
                asset_id: format!("asset{n}"),
                asset_version_id: format!("version{n}"),
                assay_id: "a".into(),
                stage: "raw".into(),
                group_id: Some("g1".into()),
                replicate_id: Some("r1".into()),
            })
            .collect(),
        target,
    }
}
fn count(path: &Path, table: &str) -> i64 {
    with_database_at(path, |c| {
        c.query_row(&format!("SELECT count(*) FROM {table}"), [], |r| r.get(0))
            .map_err(Into::into)
    })
    .unwrap()
}
fn target_two() -> AssetScope {
    scope("a", "raw", Some("g2"), Some("r2"))
}

#[test]
fn s5_ten_links_copy_cross_group_keeps_blob_sha_and_biological_n_after_reopen() {
    let (dir, path) = fixture();
    let request = request(
        "copy",
        "copy-ten",
        &(0..10).collect::<Vec<_>>(),
        target_two(),
    );
    let result = execute_at(&path, request).unwrap();
    assert_eq!(result.links.len(), 10);
    assert!(result
        .links
        .iter()
        .all(|link| link.source_link_id != link.result_link_id));
    assert_eq!(count(&path, "asset_links"), 20);
    assert_eq!(count(&path, "blobs"), 10);
    assert_eq!(count(&path, "asset_versions"), 10);
    assert_eq!(count(&path, "replicates"), 3);
    assert_eq!(count(&path, "audit_events"), 1);
    with_database_at(&path, |c| {
        let in_target: i64 = c.query_row(
            "SELECT count(*) FROM asset_links WHERE group_id='g2' AND replicate_id='r2'
             AND stage='raw' AND deleted_at IS NULL",
            [],
            |r| r.get(0),
        )?;
        assert_eq!(in_target, 10);
        for n in 0..10 {
            let bytes = fs::read(dir.path().join(format!("blob-{n}")))
                .map_err(|_| failure("DATABASE_UNAVAILABLE"))?;
            let digest = format!("{:x}", Sha256::digest(&bytes));
            let stored: String = c.query_row(
                "SELECT blob_sha256 FROM asset_versions WHERE id=?1",
                [format!("version{n}")],
                |r| r.get(0),
            )?;
            assert_eq!(digest, stored);
        }
        Ok(())
    })
    .unwrap();
}

#[test]
fn s5_move_same_asset_between_samples_preserves_link_identity() {
    let (_dir, path) = fixture();
    let destination = scope("a", "processed", Some("g1"), Some("r3"));
    let result = execute_at(&path, request("move", "move-one", &[0], destination)).unwrap();
    assert_eq!(result.links[0].result_link_id, "link0");
    assert_eq!(count(&path, "asset_links"), 10);
    assert_eq!(count(&path, "replicates"), 3);
    with_database_at(&path, |c| {
        let (group, replicate, stage): (String, String, String) = c.query_row(
            "SELECT group_id,replicate_id,stage FROM asset_links WHERE id='link0'",
            [],
            |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?)),
        )?;
        assert_eq!(
            (group.as_str(), replicate.as_str(), stage.as_str()),
            ("g1", "r3", "processed")
        );
        Ok(())
    })
    .unwrap();
}

#[test]
fn s5_conflict_rejects_entire_multi_file_batch_without_partial_writes() {
    let (_dir, path) = fixture();
    execute_at(&path, request("copy", "prime", &[9], target_two())).unwrap();
    let before = count(&path, "asset_links");
    let error = execute_at(
        &path,
        request("copy", "conflict-batch", &[0, 1, 9], target_two()),
    )
    .unwrap_err();
    assert_eq!(error.code, "NAME_CONFLICT");
    assert_eq!(count(&path, "asset_links"), before);
    assert_eq!(count(&path, "audit_events"), 1);
    assert_eq!(count(&path, "s1_requests"), 1);
    let duplicate = request("copy", "duplicate", &[0, 0], target_two());
    assert_eq!(
        execute_at(&path, duplicate).unwrap_err().code,
        "INVALID_REQUEST"
    );
}

#[test]
fn s5_request_replay_is_idempotent_and_payload_change_is_rejected() {
    let (_dir, path) = fixture();
    let first = request("copy", "same-token", &[1, 2], target_two());
    let response = execute_at(&path, first.clone()).unwrap();
    assert_eq!(execute_at(&path, first.clone()).unwrap(), response);
    assert_eq!(count(&path, "asset_links"), 12);
    assert_eq!(count(&path, "audit_events"), 1);
    let mut changed = first;
    changed.target = scope("a", "display", None, None);
    assert_eq!(
        execute_at(&path, changed).unwrap_err().code,
        "REQUEST_CONFLICT"
    );
    assert_eq!(count(&path, "asset_links"), 12);
}

#[test]
fn s5_invalid_target_stale_context_and_wrong_version_are_rejected() {
    let (_dir, path) = fixture();
    let invalid = scope("a", "raw", Some("g2"), Some("r1"));
    assert_eq!(
        execute_at(&path, request("move", "bad-target", &[1], invalid))
            .unwrap_err()
            .code,
        "INVALID_TARGET"
    );
    let mut stale = request("copy", "wrong-version", &[0], target_two());
    stale.items[0].asset_version_id = "version9".into();
    assert_eq!(
        execute_at(&path, stale).unwrap_err().code,
        "STALE_SELECTION"
    );
    let mut wrong_context = request("move", "wrong-link", &[0], target_two());
    wrong_context.items[0].replicate_id = Some("r3".into());
    assert_eq!(
        execute_at(&path, wrong_context).unwrap_err().code,
        "STALE_SELECTION"
    );
    assert_eq!(count(&path, "asset_links"), 10);
    assert_eq!(count(&path, "audit_events"), 0);
}

#[test]
fn s5_cross_assay_move_requires_valid_active_sample_and_survives_reopen() {
    let (_dir, path) = fixture();
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO assays(id,theme_id,name) VALUES('a2','t','Next Assay')",
            [],
        )?;
        c.execute(
            "INSERT INTO experiment_groups(id,assay_id,name) VALUES('g3','a2','Other Group')",
            [],
        )?;
        c.execute(
            "INSERT INTO replicates(id,group_id,label) VALUES('r4','g3','R4')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    let target = scope("a2", "raw", Some("g3"), Some("r4"));
    execute_at(&path, request("move", "cross-assay", &[3], target)).unwrap();
    with_database_at(&path, |c| {
        let assay: String = c.query_row(
            "SELECT assay_id FROM asset_links WHERE id='link3'",
            [],
            |r| r.get(0),
        )?;
        assert_eq!(assay, "a2");
        Ok(())
    })
    .unwrap();
    assert_eq!(count(&path, "replicates"), 4);
    assert_eq!(count(&path, "blobs"), 10);
}
