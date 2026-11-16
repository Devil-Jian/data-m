//! Exercises the registered Tauri command against disposable, persistent SQLite.
//! MockRuntime replaces the window runtime, not the repository or database.
use crate::project_tree::{self, TestDatabasePath};
use serde_json::{json, Value};
use std::path::Path;
use tauri::test::{get_ipc_response, mock_builder, mock_context, noop_assets, MockRuntime};

fn window(path: &Path) -> (tauri::App<MockRuntime>, tauri::WebviewWindow<MockRuntime>) {
    let app = mock_builder()
        .manage(TestDatabasePath(path.to_path_buf()))
        .invoke_handler(tauri::generate_handler![project_tree::project_tree])
        .build(mock_context(noop_assets()))
        .unwrap();
    let view = tauri::WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    (app, view)
}

fn call(view: &tauri::WebviewWindow<MockRuntime>, request: Value) -> Result<Value, Value> {
    get_ipc_response(
        view,
        tauri::webview::InvokeRequest {
            cmd: "project_tree".into(),
            callback: tauri::ipc::CallbackFn(0),
            error: tauri::ipc::CallbackFn(1),
            url: "http://tauri.localhost".parse().unwrap(),
            body: tauri::ipc::InvokeBody::Json(json!({"request": request})),
            headers: Default::default(),
            invoke_key: tauri::test::INVOKE_KEY.to_string(),
        },
    )
    .map(|body| body.deserialize::<Value>().unwrap())
}

#[test]
fn s1_ipc_serializes_crud_and_reopens_the_same_temporary_database() {
    let fixture = tempfile::tempdir().unwrap();
    let path = fixture.path().join("project-tree.sqlite");
    let (app, view) = window(&path);
    assert_eq!(
        call(&view, json!({"action":"list"})).unwrap(),
        json!({"nodes":[]})
    );
    let create =
        json!({"action":"create","kind":"project","name":"Project A","requestId":"ipc-project"});
    let created = call(&view, create.clone()).unwrap();
    assert_eq!(created["nodes"].as_array().unwrap().len(), 1);
    assert_eq!(call(&view, create).unwrap(), created);
    let project_id = created["nodes"][0]["id"].as_str().unwrap();
    let with_theme = call(&view, json!({"action":"create","kind":"theme","parentId":project_id,"name":"Theme A","requestId":"ipc-theme"})).unwrap();
    assert_eq!(with_theme["nodes"].as_array().unwrap().len(), 2);
    let theme_id = with_theme["nodes"]
        .as_array()
        .unwrap()
        .iter()
        .find(|node| node["kind"] == "theme")
        .unwrap()["id"]
        .as_str()
        .unwrap();
    let with_assay = call(&view, json!({"action":"create","kind":"assay","parentId":theme_id,"name":"Assay A","requestId":"ipc-assay"})).unwrap();
    let assay_id = with_assay["nodes"]
        .as_array()
        .unwrap()
        .iter()
        .find(|node| node["kind"] == "assay")
        .unwrap()["id"]
        .as_str()
        .unwrap();
    let renamed = call(&view, json!({"action":"rename","kind":"assay","id":assay_id,"name":"Renamed assay","requestId":"ipc-rename"})).unwrap();
    assert!(renamed["nodes"]
        .as_array()
        .unwrap()
        .iter()
        .any(|node| node["name"] == "Renamed assay"));
    assert!(call(
        &view,
        json!({"action":"delete","kind":"project","id":project_id,"requestId":"ipc-nonempty"})
    )
    .is_err());
    assert!(call(&view, json!({"action":"create","kind":"project","name":"Changed payload","requestId":"ipc-project"})).is_err());
    let snapshot = call(&view, json!({"action":"list"})).unwrap();
    assert_eq!(snapshot, renamed);
    drop(view);
    drop(app);
    let (_reopened, view) = window(&path);
    assert_eq!(call(&view, json!({"action":"list"})).unwrap(), snapshot);
    for (kind, id, request_id) in [
        ("assay", assay_id, "delete-assay"),
        ("theme", theme_id, "delete-theme"),
        ("project", project_id, "delete-project"),
    ] {
        call(
            &view,
            json!({"action":"delete","kind":kind,"id":id,"requestId":request_id}),
        )
        .unwrap();
    }
    assert_eq!(
        call(&view, json!({"action":"list"})).unwrap(),
        json!({"nodes":[]})
    );
}

#[test]
fn s1_ipc_rejects_invalid_requests_without_creating_nodes() {
    let fixture = tempfile::tempdir().unwrap();
    let (_app, view) = window(&fixture.path().join("project-tree.sqlite"));
    for request in [
        json!({"action":"create","kind":"project","name":"","requestId":"empty"}),
        json!({"action":"create","kind":"theme","name":"Theme","parentId":"missing","requestId":"parent"}),
        json!({"action":"create","kind":"project","name":"No request ID"}),
        json!({"action":"execute_sql","sql":"DROP TABLE projects"}),
    ] {
        assert!(call(&view, request).is_err());
    }
    assert_eq!(
        call(&view, json!({"action":"list"})).unwrap(),
        json!({"nodes":[]})
    );
}
