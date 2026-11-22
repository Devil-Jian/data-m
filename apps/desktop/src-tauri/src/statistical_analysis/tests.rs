use super::*;
use rusqlite::Connection;

fn db() -> Connection {
    let c = Connection::open_in_memory().unwrap();
    c.pragma_update(None, "foreign_keys", "ON").unwrap();
    c.execute_batch(include_str!("../../migrations/0001_initial.sql"))
        .unwrap();
    c.execute_batch(include_str!("../../migrations/0002_import_jobs.sql"))
        .unwrap();
    c.execute_batch(include_str!("../../migrations/0003_statistics.sql"))
        .unwrap();
    c.execute_batch(
        "INSERT INTO projects(id,name) VALUES('p','Study');
         INSERT INTO themes(id,project_id,name) VALUES('t','p','T');
         INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay'),('other','t','Other');
         INSERT INTO experiment_groups(id,assay_id,name)
            VALUES('g','a','Control'),('g2','other','Other');
         INSERT INTO blobs(sha256,size_bytes) VALUES(
           'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',4);
         INSERT INTO data_assets(id,name) VALUES('asset','input.dat');
         INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
           VALUES('v1','asset',1,
           'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa','input.dat');
         INSERT INTO asset_links(id,asset_id,assay_id,stage,display_name)
           VALUES('link','asset','a','statistics','input.dat');",
    )
    .unwrap();
    for i in 1..=6 {
        c.execute(
            "INSERT INTO replicates(id,group_id,label) VALUES(?1,'g',?2)",
            params![format!("r{i}"), format!("Replicate {i}")],
        )
        .unwrap();
    }
    c.execute(
        "INSERT INTO replicates(id,group_id,label) VALUES('x','g2','Other')",
        [],
    )
    .unwrap();
    c
}
fn request(action: &str, id: Option<&str>, samples: &[&str]) -> AnalysisRequest {
    AnalysisRequest {
        action: action.into(),
        assay_id: "a".into(),
        analysis_id: id.map(str::to_owned),
        name: if matches!(action, "create" | "save") {
            Some("Analysis".into())
        } else {
            None
        },
        notes: if matches!(action, "create" | "save") {
            Some("Notes".into())
        } else {
            None
        },
        replicate_ids: if matches!(action, "create" | "save") {
            Some(samples.iter().map(|v| v.to_string()).collect())
        } else {
            None
        },
        input_version_ids: if matches!(action, "create" | "save") {
            Some(vec!["v1".into()])
        } else {
            None
        },
        result_version_ids: if matches!(action, "create" | "save") {
            Some(vec![])
        } else {
            None
        },
    }
}
fn invoke(c: &mut Connection, r: AnalysisRequest) -> Result<String, TreeError> {
    validate(&r)?;
    assay_valid(c, &r.assay_id)?;
    let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
    let id = mutate(&tx, &r)?;
    tx.commit()?;
    Ok(id)
}
#[test]
fn s8_two_independent_analyses_keep_experiment_n() {
    let mut c = db();
    let a = invoke(
        &mut c,
        request("create", None, &["r1", "r2", "r3", "r4", "r5"]),
    )
    .unwrap();
    let b = invoke(
        &mut c,
        request("create", None, &["r1", "r2", "r3", "r4", "r5", "r6"]),
    )
    .unwrap();
    assert_ne!(a, b);
    let rows = snapshot(&c, "a", Some(a.clone())).unwrap();
    assert_eq!(
        rows.analyses.iter().find(|r| r.id == a).unwrap().included_n,
        5
    );
    assert_eq!(
        rows.analyses.iter().find(|r| r.id == b).unwrap().included_n,
        6
    );
    assert_eq!(rows.samples.len(), 6);
    let n: i64 = c
        .query_row(
            "SELECT count(*) FROM replicates WHERE group_id='g'",
            [],
            |r| r.get(0),
        )
        .unwrap();
    assert_eq!(n, 6);
}
#[test]
fn s8_copying_asset_link_does_not_change_inclusion() {
    let mut c = db();
    let a = invoke(
        &mut c,
        request("create", None, &["r1", "r2", "r3", "r4", "r5"]),
    )
    .unwrap();
    c.execute(
        "INSERT INTO asset_links(id,asset_id,assay_id,stage,display_name)
         VALUES('copy','asset','a','raw','copied.dat')",
        [],
    )
    .unwrap();
    assert_eq!(
        snapshot(&c, "a", Some(a)).unwrap().analyses[0].included_n,
        5
    );
    assert_eq!(
        c.query_row(
            "SELECT count(*) FROM replicates WHERE group_id='g'",
            [],
            |r| r.get::<_, i64>(0)
        )
        .unwrap(),
        6
    );
}
#[test]
fn s8_reject_cross_assay_and_rollback() {
    let mut c = db();
    assert!(invoke(&mut c, request("create", None, &["r1", "x"])).is_err());
    assert_eq!(
        c.query_row("SELECT count(*) FROM statistical_analyses", [], |r| r
            .get::<_, i64>(0))
            .unwrap(),
        0
    );
    c.execute(
        "INSERT INTO statistical_analyses(id,assay_id,name) VALUES('a1','a','A')",
        [],
    )
    .unwrap();
    assert!(c
        .execute(
            "INSERT INTO analysis_samples(analysis_id,replicate_id,included)
        VALUES('a1','x',1)",
            []
        )
        .is_err());
}
#[test]
fn s8_frozen_references_are_fixed_and_history_survives_soft_delete() {
    let mut c = db();
    let id = invoke(&mut c, request("create", None, &["r1"])).unwrap();
    invoke(&mut c, request("freeze", Some(&id), &[])).unwrap();
    assert!(invoke(&mut c, request("save", Some(&id), &["r2"])).is_err());
    assert!(c
        .execute(
            "DELETE FROM analysis_asset_versions WHERE analysis_id=?1",
            [&id]
        )
        .is_err());
    c.execute(
        "UPDATE asset_links SET deleted_at='2026-11-17' WHERE id='link'",
        [],
    )
    .unwrap();
    let state = snapshot(&c, "a", Some(id)).unwrap();
    assert_eq!(state.input_version_ids, vec!["v1"]);
    assert!(
        state
            .available_versions
            .iter()
            .find(|v| v.id == "v1")
            .unwrap()
            .deleted
    );
}
#[test]
fn s8_revise_frozen_preserves_old_analysis() {
    let mut c = db();
    let id = invoke(&mut c, request("create", None, &["r1", "r2"])).unwrap();
    invoke(&mut c, request("freeze", Some(&id), &[])).unwrap();
    let revision = invoke(&mut c, request("revise", Some(&id), &[])).unwrap();
    assert_ne!(id, revision);
    assert_eq!(
        snapshot(&c, "a", Some(id.clone()))
            .unwrap()
            .input_version_ids,
        vec!["v1"]
    );
    assert_eq!(snapshot(&c, "a", Some(revision)).unwrap().analyses.len(), 2);
    assert!(status(&c, "a", &id).unwrap());
}
#[test]
fn s8_failed_version_selection_rolls_back_draft_save() {
    let mut c = db();
    let id = invoke(&mut c, request("create", None, &["r1"])).unwrap();
    let mut r = request("save", Some(&id), &["r2"]);
    r.input_version_ids = Some(vec!["not_a_version".into()]);
    assert!(invoke(&mut c, r).is_err());
    let state = snapshot(&c, "a", Some(id)).unwrap();
    assert!(
        state
            .samples
            .iter()
            .find(|r| r.replicate_id == "r1")
            .unwrap()
            .included
    );
    assert!(
        !state
            .samples
            .iter()
            .find(|r| r.replicate_id == "r2")
            .unwrap()
            .included
    );
}

#[test]
fn s8_migrate_existing_v2_preserves_analysis_and_original_ids() {
    let dir = tempfile::tempdir().unwrap();
    let db_path = dir.path().join("project-tree.sqlite");
    {
        let c = Connection::open(&db_path).unwrap();
        c.pragma_update(None, "foreign_keys", "ON").unwrap();
        c.execute_batch(include_str!("../../migrations/0001_initial.sql"))
            .unwrap();
        c.execute_batch(crate::project_tree::REQUEST_SCHEMA)
            .unwrap();
        c.execute_batch(include_str!("../../migrations/0002_import_jobs.sql"))
            .unwrap();
        c.pragma_update(None, "application_id", 0x444d5331_i64)
            .unwrap();
        c.pragma_update(None, "user_version", 2).unwrap();
        c.execute_batch(
            "INSERT INTO projects(id,name) VALUES('p','P');
             INSERT INTO themes(id,project_id,name) VALUES('t','p','T');
             INSERT INTO assays(id,theme_id,name) VALUES('a','t','A');
             INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Control');
             INSERT INTO replicates(id,group_id,label) VALUES('r1','g','R1');
             INSERT INTO statistical_analyses(id,assay_id,name) VALUES('analysis-old','a','Original');
             INSERT INTO analysis_samples(analysis_id,replicate_id,included)
               VALUES('analysis-old','r1',1);"
        ).unwrap();
    }
    crate::project_tree::with_database_at(&db_path, |c| {
        let version: i64 = c.pragma_query_value(None, "user_version", |r| r.get(0))?;
        // S14 extends the recognized database to schema v4; the S8 IDs and
        // sample inclusion must survive the complete v2 -> v3 -> v4 migration.
        assert_eq!(version, 4);
        let document_links: i64 = c.query_row(
            "SELECT count(*) FROM project_document_links", [], |r| r.get(0)
        )?;
        assert_eq!(document_links, 0);
        let state = snapshot(c, "a", Some("analysis-old".into()))?;
        assert_eq!(state.analyses[0].id, "analysis-old");
        assert_eq!(state.analyses[0].included_n, 1);
        Ok(())
    })
    .unwrap();
    // A fresh connection observes the committed schema and original stable IDs.
    crate::project_tree::with_database_at(&db_path, |c| {
        assert_eq!(
            snapshot(c, "a", Some("analysis-old".into()))?.analyses[0].included_n,
            1
        );
        Ok(())
    })
    .unwrap();
}
