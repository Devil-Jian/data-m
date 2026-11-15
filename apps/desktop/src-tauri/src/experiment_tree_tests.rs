use super::*;
use serde_json::{json, Value};
use tauri::test::{get_ipc_response, mock_builder, mock_context, noop_assets, MockRuntime};

fn fixture() -> (tempfile::TempDir, std::path::PathBuf) {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("structure.sqlite");
    with_database_at(&path, |connection| {
        connection.execute_batch("INSERT INTO projects(id,name) VALUES('p','Project'); INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme'); INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay A'),('b','t','Assay B');")?;
        Ok(())
    }).unwrap();
    (dir, path)
}

fn request(value: Value) -> ExperimentRequest {
    serde_json::from_value(value).unwrap()
}
fn list(path: &Path, assay: &str) -> ExperimentSnapshot {
    execute_at(path, request(json!({"action":"list","assayId":assay}))).unwrap()
}
fn group(path: &Path, assay: &str, name: &str, token: &str) -> String {
    let snapshot = execute_at(
        path,
        request(
            json!({"action":"create","assayId":assay,"kind":"group","name":name,"requestId":token}),
        ),
    )
    .unwrap();
    snapshot
        .groups
        .iter()
        .find(|g| g.name == name)
        .unwrap()
        .id
        .clone()
}
fn replicate(path: &Path, assay: &str, group: &str, name: &str, token: &str) -> String {
    let snapshot=execute_at(path,request(json!({"action":"create","assayId":assay,"kind":"replicate","parentId":group,"name":name,"requestId":token}))).unwrap();
    snapshot
        .groups
        .iter()
        .find(|g| g.id == group)
        .unwrap()
        .replicates
        .iter()
        .find(|r| r.label == name)
        .unwrap()
        .id
        .clone()
}

#[test]
fn s2_three_groups_six_samples_persist_with_stable_ids_and_eighteen_total() {
    let (_dir, path) = fixture();
    for g in 0..3 {
        let id = group(&path, "a", &format!("Group {g}"), &format!("g-{g}"));
        for r in 0..6 {
            replicate(
                &path,
                "a",
                &id,
                &format!("Sample {r}"),
                &format!("r-{g}-{r}"),
            );
        }
    }
    let persisted = list(&path, "a");
    assert_eq!(persisted.groups.len(), 3);
    assert_eq!(persisted.replicate_count, 18);
    assert!(persisted.groups.iter().all(|g| g.replicates.len() == 6));
    assert_eq!(list(&path, "a"), persisted);
    assert_eq!(list(&path, "b").replicate_count, 0);
    with_database_at(&path, |connection| {
        let counts = connection.prepare("SELECT count(*) FROM replicates r JOIN experiment_groups g ON g.id=r.group_id WHERE g.assay_id='a' AND g.deleted_at IS NULL AND r.deleted_at IS NULL GROUP BY g.id")?
            .query_map([], |row| row.get::<_, i64>(0))?.collect::<rusqlite::Result<Vec<_>>>()?;
        assert_eq!(counts, vec![6,6,6]);
        assert_eq!(connection.query_row("SELECT count(*) FROM replicates", [], |row|row.get::<_,i64>(0))?,18);
        Ok(())
    }).unwrap();
}

#[test]
fn s2_empty_stage_containers_have_fixed_scopes_and_no_samples() {
    let (_dir, path) = fixture();
    let snapshot = list(&path, "a");
    assert!(snapshot.groups.is_empty());
    assert_eq!(snapshot.replicate_count, 0);
    assert_eq!(
        serde_json::to_value(snapshot.stages).unwrap(),
        json!([
            {"id":"raw","label":"Raw","scope":"replicate"},
            {"id":"processed","label":"Processed","scope":"replicate"},
            {"id":"statistics","label":"Statistics","scope":"assay"},
            {"id":"display","label":"Display","scope":"assay"}
        ])
    );
}

#[test]
fn s2_cross_assay_and_move_requests_fail_without_nodes_or_retry_records() {
    let (_dir, path) = fixture();
    let g = group(&path, "b", "Foreign", "foreign-group");
    let r = replicate(&path, "b", &g, "Foreign sample", "foreign-sample");
    let before_a = list(&path, "a");
    let before_b = list(&path, "b");
    for payload in [
        json!({"action":"create","assayId":"a","kind":"replicate","parentId":g,"name":"Wrong","requestId":"failed"}),
        json!({"action":"rename","assayId":"a","kind":"group","id":g,"name":"Wrong","requestId":"failed"}),
        json!({"action":"delete","assayId":"a","kind":"replicate","id":r,"requestId":"failed"}),
        json!({"action":"rename","assayId":"b","kind":"replicate","id":r,"parentId":"move-target","name":"Move","requestId":"failed"}),
        json!({"action":"create","assayId":"missing","kind":"group","name":"Wrong","requestId":"failed"}),
    ] {
        assert!(execute_at(&path, request(payload)).is_err());
        assert_eq!(list(&path, "a"), before_a);
        assert_eq!(list(&path, "b"), before_b);
    }
    with_database_at(&path, |connection| {
        assert_eq!(
            connection.query_row(
                "SELECT count(*) FROM s1_requests WHERE request_id='failed'",
                [],
                |r| r.get::<_, i64>(0)
            )?,
            0
        );
        Ok(())
    })
    .unwrap();
    group(&path, "a", "Valid after failure", "failed");
}

#[test]
fn s2_idempotency_compares_payload_and_assay_scope_and_returns_current_snapshot() {
    let (_dir, path) = fixture();
    let create = request(
        json!({"action":"create","assayId":"a","kind":"group","name":"Original","requestId":"same"}),
    );
    let first = execute_at(&path, create.clone()).unwrap();
    assert_eq!(execute_at(&path, create.clone()).unwrap(), first);
    for payload in [
        json!({"action":"create","assayId":"a","kind":"group","name":"Different","requestId":"same"}),
        json!({"action":"create","assayId":"b","kind":"group","name":"Original","requestId":"same"}),
    ] {
        assert_eq!(
            execute_at(&path, request(payload)).unwrap_err().code,
            "REQUEST_CONFLICT"
        );
    }
    let id = &first.groups[0].id;
    let renamed=execute_at(&path,request(json!({"action":"rename","assayId":"a","kind":"group","id":id,"name":"Renamed 🧪","requestId":"rename"}))).unwrap();
    assert_eq!(execute_at(&path, create).unwrap(), renamed);
    assert_eq!(renamed.groups[0].id, *id);
    assert_eq!(renamed.groups[0].name, "Renamed 🧪");
    assert!(list(&path, "b").groups.is_empty());
}

#[test]
fn s2_soft_delete_guards_nonempty_groups_and_asset_copy_does_not_create_samples() {
    let (_dir, path) = fixture();
    let g = group(&path, "a", "Group", "g");
    let r = replicate(&path, "a", &g, "Sample", "r");
    let delete_group =
        request(json!({"action":"delete","assayId":"a","kind":"group","id":g,"requestId":"dg"}));
    let delete_rep = request(
        json!({"action":"delete","assayId":"a","kind":"replicate","id":r,"requestId":"dr"}),
    );
    assert_eq!(
        execute_at(&path, delete_group.clone()).unwrap_err().code,
        "NODE_IN_USE"
    );
    with_database_at(&path,|c| {
        c.execute("INSERT INTO data_assets(id,name) VALUES('asset','file.tif')",[])?;
        for (id,name) in [("link1","file.tif"),("link2","copy.tif")] {
            c.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name) VALUES(?1,'asset','a',?2,?3,'raw',?4)",params![id,g,r,name])?;
        }
        Ok(())
    }).unwrap();
    assert_eq!(list(&path, "a").replicate_count, 1);
    assert_eq!(
        execute_at(&path, delete_rep.clone()).unwrap_err().code,
        "NODE_IN_USE"
    );
    with_database_at(&path, |c| {
        c.execute("UPDATE asset_links SET deleted_at='deleted'", [])?;
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute_at(&path, delete_rep.clone())
            .unwrap()
            .replicate_count,
        0
    );
    assert_eq!(execute_at(&path, delete_rep).unwrap().replicate_count, 0);
    assert!(execute_at(&path, delete_group.clone())
        .unwrap()
        .groups
        .is_empty());
    assert!(execute_at(&path, delete_group).unwrap().groups.is_empty());
    with_database_at(&path, |c| {
        assert_eq!(
            c.query_row(
                "SELECT count(*) FROM replicates WHERE deleted_at IS NOT NULL",
                [],
                |r| r.get::<_, i64>(0)
            )?,
            1
        );
        Ok(())
    })
    .unwrap();
    assert!(execute_at(&path,request(json!({"action":"create","assayId":"a","kind":"replicate","parentId":g,"name":"Orphan","requestId":"orphan"}))).is_err());
}

#[test]
fn s2_invalid_names_shapes_and_unknown_database_are_rejected_safely() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("unknown.sqlite");
    std::fs::write(&path, b"private existing file").unwrap();
    let bytes = std::fs::read(&path).unwrap();
    assert_eq!(
        execute_at(&path, request(json!({"action":"list","assayId":"a"})))
            .unwrap_err()
            .code,
        "UNKNOWN_DATABASE"
    );
    assert_eq!(std::fs::read(&path).unwrap(), bytes);
    for name in ["", " \u{3000}", "bad\nname", "bad\u{202e}name"] {
        let error=execute_at(&path,request(json!({"action":"create","assayId":"a","kind":"group","name":name,"requestId":"bad"}))).unwrap_err();
        assert_eq!(error.code, "INVALID_NAME");
        assert!(!serde_json::to_string(&error)
            .unwrap()
            .contains("unknown.sqlite"));
    }
    for payload in [
        json!({"action":"list","assayId":""}),
        json!({"action":"create","assayId":"a","kind":"group","name":"G"}),
        json!({"action":"create","assayId":"a","kind":"group","parentId":"a","name":"G","requestId":"1"}),
        json!({"action":"delete","assayId":"a","kind":"../../groups","id":"g","requestId":"1"}),
    ] {
        assert_eq!(
            execute_at(&path, request(payload)).unwrap_err().code,
            "INVALID_REQUEST"
        );
    }
    assert!(serde_json::from_value::<ExperimentRequest>(
        json!({"action":"list","assayId":"a","databasePath":"private.sqlite"})
    )
    .is_err());
}

fn window(path: &Path) -> (tauri::App<MockRuntime>, tauri::WebviewWindow<MockRuntime>) {
    let app = mock_builder()
        .manage(crate::project_tree::TestDatabasePath(path.to_path_buf()))
        .invoke_handler(tauri::generate_handler![
            crate::experiment_tree::experiment_tree
        ])
        .build(mock_context(noop_assets()))
        .unwrap();
    let view = tauri::WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    (app, view)
}
fn ipc(view: &tauri::WebviewWindow<MockRuntime>, payload: Value) -> Result<Value, Value> {
    get_ipc_response(
        view,
        tauri::webview::InvokeRequest {
            cmd: "experiment_tree".into(),
            callback: tauri::ipc::CallbackFn(0),
            error: tauri::ipc::CallbackFn(1),
            url: "http://tauri.localhost".parse().unwrap(),
            body: tauri::ipc::InvokeBody::Json(json!({"request":payload})),
            headers: Default::default(),
            invoke_key: tauri::test::INVOKE_KEY.to_string(),
        },
    )
    .map(|body| body.deserialize::<Value>().unwrap())
}

#[test]
fn s2_native_handler_crud_and_reopen_use_real_temporary_sqlite() {
    let (_dir, path) = fixture();
    let (app, view) = window(&path);
    let groups=ipc(&view,json!({"action":"create","assayId":"a","kind":"group","name":"Control","requestId":"ipc-g"})).unwrap();
    let g = groups["groups"][0]["id"].as_str().unwrap();
    let samples=ipc(&view,json!({"action":"create","assayId":"a","kind":"replicate","parentId":g,"name":"R1","requestId":"ipc-r"})).unwrap();
    let r = samples["groups"][0]["replicates"][0]["id"]
        .as_str()
        .unwrap();
    assert_eq!(samples["replicateCount"], 1);
    let renamed=ipc(&view,json!({"action":"rename","assayId":"a","kind":"replicate","id":r,"name":"R1 renamed","requestId":"ipc-rename"})).unwrap();
    assert_eq!(renamed["groups"][0]["replicates"][0]["label"], "R1 renamed");
    assert!(ipc(
        &view,
        json!({"action":"delete","assayId":"a","kind":"group","id":g,"requestId":"ipc-blocked"})
    )
    .is_err());
    drop(view);
    drop(app);
    let (_app, view) = window(&path);
    assert_eq!(
        ipc(&view, json!({"action":"list","assayId":"a"})).unwrap(),
        renamed
    );
    ipc(
        &view,
        json!({"action":"delete","assayId":"a","kind":"replicate","id":r,"requestId":"ipc-dr"}),
    )
    .unwrap();
    assert!(ipc(
        &view,
        json!({"action":"delete","assayId":"a","kind":"group","id":g,"requestId":"ipc-dg"})
    )
    .unwrap()["groups"]
        .as_array()
        .unwrap()
        .is_empty());
}
