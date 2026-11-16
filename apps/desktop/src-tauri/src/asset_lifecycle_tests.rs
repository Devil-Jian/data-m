use super::*;
use crate::asset_recycle::{list_at, RecycleQuery};
use sha2::{Digest, Sha256};
use std::{fs, path::PathBuf};
use tempfile::TempDir;

fn fixture() -> (TempDir, PathBuf) {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("s6.sqlite");
    with_database_at(&path,|c|{
        c.execute("INSERT INTO projects(id,name) VALUES('p','Project')",[])?;
        c.execute("INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme')",[])?;
        c.execute("INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay')",[])?;
        c.execute("INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Group')",[])?;
        c.execute("INSERT INTO replicates(id,group_id,label) VALUES('r','g','R1')",[])?;
        c.execute("INSERT INTO replicates(id,group_id,label) VALUES('r2','g','R2')",[])?;
        for n in 0..10 {
            let bytes=format!("S6 preserved source bytes {n}").into_bytes();
            fs::write(dir.path().join(format!("source{n}")), &bytes)
                .map_err(|_|fail("INVALID_REQUEST"))?;
            let sha=format!("{:x}",Sha256::digest(&bytes));
            c.execute("INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",params![sha,bytes.len() as i64])?;
            c.execute("INSERT INTO data_assets(id,name) VALUES(?1,?2)",
                params![format!("asset{n}"),format!("raw{n}.png")])?;
            c.execute("INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
                VALUES(?1,?2,1,?3,?4)",
                params![format!("version{n}"),format!("asset{n}"),sha,format!("raw{n}.png")])?;
            c.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
                VALUES(?1,?2,'a','g','r','raw',?3)",
                params![format!("link{n}"),format!("asset{n}"),format!("raw{n}.png")])?;
        }
        Ok(())
    }).unwrap();
    (dir, path)
}
fn identity(n: usize) -> LinkIdentity {
    LinkIdentity {
        asset_link_id: format!("link{n}"),
        asset_id: format!("asset{n}"),
        asset_version_id: format!("version{n}"),
        assay_id: "a".into(),
        stage: "raw".into(),
        group_id: Some("g".into()),
        replicate_id: Some("r".into()),
    }
}
fn action(a: &str, token: &str, indices: &[usize], name: Option<&str>) -> LifecycleRequest {
    let items = indices.iter().map(|n| identity(*n)).collect::<Vec<_>>();
    LifecycleRequest {
        action: a.into(),
        request_id: token.into(),
        item: if a == "rename" {
            items.first().cloned()
        } else {
            None
        },
        items: if a == "rename" { None } else { Some(items) },
        new_name: name.map(String::from),
    }
}
fn count(path: &Path, sql: &str) -> i64 {
    with_database_at(path, |c| {
        c.query_row(sql, [], |r| r.get(0)).map_err(Into::into)
    })
    .unwrap()
}
fn trash(path: &Path) -> crate::asset_recycle::RecyclePage {
    let q: RecycleQuery =
        serde_json::from_str(r#"{"assayId":"a","stage":"raw","groupId":"g","replicateId":"r"}"#)
            .unwrap();
    list_at(path, q).unwrap()
}
#[test]
fn s6_rename_one_link_preserves_other_context_and_asset_version_blob_n() {
    let (_dir, path) = fixture();
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
            VALUES('extra','asset0','a','g','r2','raw','original.png')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    let result = execute_at(
        &path,
        action("rename", "r-one", &[0], Some("处理后的图像.PNG")),
    )
    .unwrap();
    assert_eq!(result.results[0].name, "处理后的图像.PNG");
    with_database_at(&path, |c| {
        let other: String = c.query_row(
            "SELECT display_name FROM asset_links WHERE id='extra'",
            [],
            |r| r.get(0),
        )?;
        let original: String =
            c.query_row("SELECT name FROM data_assets WHERE id='asset0'", [], |r| {
                r.get(0)
            })?;
        assert_eq!(other, "original.png");
        assert_eq!(original, "raw0.png");
        Ok(())
    })
    .unwrap();
    assert_eq!(count(&path, "SELECT count(*) FROM blobs"), 10);
    assert_eq!(count(&path, "SELECT count(*) FROM asset_versions"), 10);
    assert_eq!(count(&path, "SELECT count(*) FROM replicates"), 2);
}
#[test]
fn s6_filename_validation_rejects_windows_devices_paths_controls_and_unicode_combining() {
    let (_dir, path) = fixture();
    for (i, name) in [
        "",
        "CON.txt",
        "NUL",
        "lpt1.csv",
        "COM¹.dat",
        "..",
        "raw?2.png",
        "a/b.png",
        "bad\\name",
        "bad. ",
        "trailing.",
        "bad\u{0001}name",
        "e\u{0301}.png",
    ]
    .iter()
    .enumerate()
    {
        let r = action("rename", &format!("invalid-{i}"), &[0], Some(name));
        assert_eq!(
            execute_at(&path, r).unwrap_err().code,
            "INVALID_NAME",
            "{name:?}"
        );
    }
    assert_eq!(count(&path, "SELECT count(*) FROM audit_events"), 0);
}
#[test]
fn s6_case_and_unicode_conflict_and_distinct_sample_same_name() {
    let (_dir, path) = fixture();
    execute_at(
        &path,
        action("rename", "r-case", &[0], Some("Échantillon.PNG")),
    )
    .unwrap();
    let conflict = execute_at(
        &path,
        action("rename", "r-conflict", &[1], Some("échantillon.png")),
    );
    assert_eq!(conflict.unwrap_err().code, "NAME_CONFLICT");
    let unchanged = execute_at(&path, action("rename", "r-ascii", &[1], Some("RAW0.png")));
    assert_eq!(unchanged.unwrap().results[0].name, "RAW0.png");
    with_database_at(&path, |c| {
        c.execute(
            "UPDATE asset_links SET replicate_id='r2' WHERE id='link2'",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    execute_at(
        &path,
        action("rename", "r-cross-sample", &[2], Some("Échantillon.PNG")),
    )
    .unwrap_err();
    // The moved link must supply its new scope; stale requests never rename.
    let mut proper = action("rename", "r-cross-sample2", &[2], Some("Échantillon.PNG"));
    proper.item.as_mut().unwrap().replicate_id = Some("r2".into());
    assert_eq!(
        execute_at(&path, proper).unwrap().results[0].name,
        "Échantillon.PNG"
    );
}
#[test]
fn s6_soft_delete_recycle_query_and_restore_reopen_keep_identity_and_sha() {
    let (dir, path) = fixture();
    let before = fs::read(dir.path().join("source0")).unwrap();
    let result = execute_at(&path, action("soft_delete", "del-one", &[0], None)).unwrap();
    assert!(result.results[0].deleted_at.is_some());
    assert_eq!(
        count(
            &path,
            "SELECT count(*) FROM asset_links WHERE id='link0' AND deleted_at IS NULL"
        ),
        0
    );
    let page = trash(&path);
    assert_eq!(page.total, 1);
    assert_eq!(page.items[0].asset_link_id, "link0");
    assert!(page.items[0].can_restore);
    let restored = execute_at(&path, action("restore", "rest-one", &[0], None)).unwrap();
    assert_eq!(restored.results[0].asset_version_id, "version0");
    assert!(restored.results[0].deleted_at.is_none());
    assert_eq!(trash(&path).total, 0);
    assert_eq!(
        count(
            &path,
            "SELECT count(*) FROM asset_links WHERE id='link0' AND deleted_at IS NULL"
        ),
        1
    );
    assert_eq!(fs::read(dir.path().join("source0")).unwrap(), before);
    assert_eq!(count(&path, "SELECT count(*) FROM blobs"), 10);
    assert_eq!(count(&path, "SELECT count(*) FROM replicates"), 2);
}
#[test]
fn s6_restore_name_conflict_does_not_overwrite_or_partially_recover() {
    let (_dir, path) = fixture();
    execute_at(&path, action("soft_delete", "del-two", &[0, 1], None)).unwrap();
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
            VALUES('taken','asset2','a','g','r','raw','RAW1.PNG')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute_at(&path, action("restore", "conflicted", &[0, 1], None))
            .unwrap_err()
            .code,
        "NAME_CONFLICT"
    );
    assert_eq!(count(&path,"SELECT count(*) FROM asset_links WHERE id IN ('link0','link1') AND deleted_at IS NOT NULL"),2);
    let page = trash(&path);
    assert_eq!(page.total, 2);
    assert!(page
        .items
        .iter()
        .any(|x| x.name == "raw1.png" && !x.can_restore));
}
#[test]
fn s6_last_active_link_of_paper_referenced_asset_is_protected_batch_aware() {
    let (_dir, path) = fixture();
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO papers(id,project_id,title) VALUES('paper','p','test')",
            [],
        )?;
        c.execute(
            "INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','1')",
            [],
        )?;
        c.execute(
            "INSERT INTO panels(id,figure_id,label) VALUES('panel','fig','A')",
            [],
        )?;
        c.execute(
            "INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
            VALUES('panel','version0','display')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute_at(&path, action("soft_delete", "protected", &[0], None))
            .unwrap_err()
            .code,
        "REFERENCE_PROTECTED"
    );
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
            VALUES('extra','asset0','a','g','r2','raw','second.png')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute_at(&path, action("soft_delete", "allowed", &[0], None))
            .unwrap()
            .results
            .len(),
        1
    );
    assert_eq!(count(&path, "SELECT count(*) FROM panel_asset_versions"), 1);
    // Existing association in another scope is still available and version pinned.
    assert_eq!(
        count(
            &path,
            "SELECT count(*) FROM asset_links WHERE id='extra' AND deleted_at IS NULL"
        ),
        1
    );
}
#[test]
fn s6_batch_reference_guard_prevents_deletion_of_last_two_links() {
    let (_dir, path) = fixture();
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO papers(id,project_id,title) VALUES('paper','p','test')",
            [],
        )?;
        c.execute(
            "INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','1')",
            [],
        )?;
        c.execute(
            "INSERT INTO panels(id,figure_id,label) VALUES('panel','fig','A')",
            [],
        )?;
        c.execute(
            "INSERT INTO representative_sources(panel_id,replicate_id,asset_version_id)
            VALUES('panel','r','version0')",
            [],
        )?;
        c.execute(
            "INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name)
            VALUES('extra','asset0','a','g','r2','raw','second.png')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    let mut r = action("soft_delete", "all-links", &[0], None);
    let mut second = identity(0);
    second.asset_link_id = "extra".into();
    second.replicate_id = Some("r2".into());
    r.items.as_mut().unwrap().push(second);
    assert_eq!(
        execute_at(&path, r).unwrap_err().code,
        "REFERENCE_PROTECTED"
    );
    assert_eq!(
        count(
            &path,
            "SELECT count(*) FROM asset_links WHERE asset_id='asset0' AND deleted_at IS NULL"
        ),
        2
    );
}
#[test]
fn s6_atomic_ten_delete_restore_and_audit_failure_rolls_back() {
    let (_dir, path) = fixture();
    let ids = (0..10).collect::<Vec<_>>();
    let first = action("soft_delete", "all-ten", &ids, None);
    assert_eq!(execute_at(&path, first).unwrap().results.len(), 10);
    assert_eq!(trash(&path).total, 10);
    with_database_at(&path, |c| {
        c.execute(
            "INSERT INTO audit_events(id,action,object_type,object_id)
          VALUES('s6:audit-collision','old','fixture','x')",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute_at(&path, action("restore", "audit-collision", &ids, None))
            .unwrap_err()
            .code,
        "DATABASE_UNAVAILABLE"
    );
    assert_eq!(trash(&path).total, 10);
    assert_eq!(count(&path, "SELECT count(*) FROM s1_requests"), 1);
    execute_at(&path, action("restore", "all-restore", &ids, None)).unwrap();
    assert_eq!(trash(&path).total, 0);
    assert_eq!(count(&path, "SELECT count(*) FROM blobs"), 10);
    assert_eq!(count(&path, "SELECT count(*) FROM asset_versions"), 10);
    assert_eq!(count(&path, "SELECT count(*) FROM replicates"), 2);
}
#[test]
fn s6_request_replay_after_reopen_is_idempotent_and_conflict_is_rejected() {
    let (_dir, path) = fixture();
    let a = action("rename", "replay-token", &[0], Some("result.png"));
    let first = execute_at(&path, a.clone()).unwrap();
    assert_eq!(execute_at(&path, a.clone()).unwrap(), first);
    let another = action("soft_delete", "replay-token", &[1], None);
    assert_eq!(
        execute_at(&path, another).unwrap_err().code,
        "REQUEST_CONFLICT"
    );
    assert_eq!(count(&path, "SELECT count(*) FROM audit_events"), 1);
    assert_eq!(count(&path, "SELECT count(*) FROM s1_requests"), 1);
}
#[test]
fn s6_stale_context_wrong_version_parent_deleted_and_recycle_state_rejected() {
    let (_dir, path) = fixture();
    let mut bad = action("soft_delete", "bad-version", &[0], None);
    bad.items.as_mut().unwrap()[0].asset_version_id = "version9".into();
    assert_eq!(execute_at(&path, bad).unwrap_err().code, "STALE_SELECTION");
    let mut bad = action("soft_delete", "bad-scope", &[0], None);
    bad.items.as_mut().unwrap()[0].replicate_id = Some("r2".into());
    assert_eq!(execute_at(&path, bad).unwrap_err().code, "STALE_SELECTION");
    execute_at(&path, action("soft_delete", "deleted", &[0], None)).unwrap();
    assert_eq!(
        execute_at(&path, action("soft_delete", "deleted-again", &[0], None))
            .unwrap_err()
            .code,
        "ALREADY_DELETED"
    );
    with_database_at(&path, |c| {
        c.execute(
            "UPDATE replicates SET deleted_at='2026-11-16' WHERE id='r'",
            [],
        )?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute_at(&path, action("restore", "deleted-parent", &[0], None))
            .unwrap_err()
            .code,
        "INVALID_TARGET"
    );
}
