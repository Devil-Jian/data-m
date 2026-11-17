use super::*;

#[test]
fn s1_owned_schema_recognition_is_independent_of_checkout_line_endings() {
    let lf = MIGRATION.replace("\r\n", "\n");
    for migration in [lf.clone(), lf.replace('\n', "\r\n")] {
        let connection = Connection::open_in_memory().unwrap();
        connection.execute_batch(&migration).unwrap();
        connection.execute_batch(REQUEST_SCHEMA).unwrap();
        connection
            .pragma_update(None, "application_id", APPLICATION_ID)
            .unwrap();
        connection.pragma_update(None, "user_version", 1).unwrap();
        recognized(&connection).unwrap();
    }
}

#[test]
fn s1_failed_initialization_never_publishes_final_database_and_can_retry() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    let failed = publish_new_database(&path, |connection| {
        connection.execute_batch("CREATE TABLE partial_initialization(id TEXT);")?;
        Err(TreeError::new(
            "DATABASE_UNAVAILABLE",
            "Injected initialization failure.",
        ))
    });
    assert!(failed.is_err());
    assert!(!path.exists());
    assert_eq!(std::fs::read_dir(dir.path()).unwrap().count(), 0);
    assert!(list(&path).nodes.is_empty());
    create(&path, "project", None, "Retry succeeded", "retry");
    assert_eq!(list(&path).nodes.len(), 1);
}

#[test]
fn s1_atomic_publication_never_replaces_an_existing_unknown_file() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    std::fs::write(&path, b"An existing file must survive publication races.").unwrap();
    let before = std::fs::read(&path).unwrap();
    publish_new_database(&path, initialize).unwrap();
    assert_eq!(std::fs::read(&path).unwrap(), before);
    assert_eq!(std::fs::read_dir(dir.path()).unwrap().count(), 1);
    assert_eq!(
        execute_at(&path, request("list", None, None, None, None, None))
            .unwrap_err()
            .code,
        "UNKNOWN_DATABASE"
    );
    assert_eq!(std::fs::read(&path).unwrap(), before);
}

fn request(
    action: &str,
    kind: Option<&str>,
    id: Option<&str>,
    parent: Option<&str>,
    name: Option<&str>,
    token: Option<&str>,
) -> TreeRequest {
    serde_json::from_value(serde_json::json!({"action":action,"kind":kind,"id":id,"parentId":parent,"name":name,"requestId":token})).unwrap()
}
fn list(path: &Path) -> TreeSnapshot {
    execute_at(path, request("list", None, None, None, None, None)).unwrap()
}
fn create(path: &Path, kind: &str, parent: Option<&str>, name: &str, token: &str) -> String {
    let before = list(path);
    let after = execute_at(
        path,
        request("create", Some(kind), None, parent, Some(name), Some(token)),
    )
    .unwrap();
    after
        .nodes
        .iter()
        .find(|node| !before.nodes.iter().any(|old| old.id == node.id))
        .unwrap()
        .id
        .clone()
}

#[test]
fn s1_disk_persistence_multiple_projects_and_stable_ids() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    assert!(list(&path).nodes.is_empty());
    let p = create(&path, "project", None, "项目 🧪", "1");
    let q = create(&path, "project", None, "Second", "2");
    let t = create(&path, "theme", Some(&p), "Theme", "3");
    create(&path, "theme", Some(&p), "Another", "4");
    let u = create(&path, "theme", Some(&q), "Other project", "5");
    let a = create(&path, "assay", Some(&t), "Assay", "6");
    create(&path, "assay", Some(&t), "Second assay", "7");
    create(&path, "assay", Some(&u), "Other assay", "8");
    let renamed = execute_at(
        &path,
        request(
            "rename",
            Some("assay"),
            Some(&a),
            None,
            Some("重命名"),
            Some("9"),
        ),
    )
    .unwrap();
    assert_eq!(renamed.nodes.len(), 8);
    assert_eq!(list(&path), renamed);
    assert!(renamed
        .nodes
        .iter()
        .any(|n| n.id == a && n.name == "重命名" && n.parent_id.as_deref() == Some(t.as_str())));
}

#[test]
fn s1_invalid_input_and_failed_requests_leave_no_partial_writes() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    let p = create(&path, "project", None, "P", "1");
    let before = list(&path);
    for bad in [
        "",
        " \u{3000}",
        "bad\nname",
        "bad\0name",
        "bad\u{7f}",
        "bad\u{202e}name",
    ] {
        assert!(execute_at(
            &path,
            request(
                "create",
                Some("project"),
                None,
                None,
                Some(bad),
                Some("bad-name")
            )
        )
        .is_err());
    }
    for bad in [
        request(
            "create",
            Some("theme"),
            None,
            Some("missing"),
            Some("T"),
            Some("invalid"),
        ),
        request(
            "create",
            Some("assay"),
            None,
            Some(&p),
            Some("A"),
            Some("invalid"),
        ),
        request(
            "create",
            Some("project"),
            None,
            Some(&p),
            Some("P"),
            Some("invalid"),
        ),
        request(
            "rename",
            Some("theme"),
            Some(&p),
            None,
            Some("Wrong kind"),
            Some("invalid"),
        ),
        request(
            "delete",
            Some("project"),
            Some(&p),
            Some("wrong-parent"),
            None,
            Some("invalid"),
        ),
        request("create", Some("project"), None, None, Some("P"), None),
    ] {
        assert!(execute_at(&path, bad).is_err());
        assert_eq!(list(&path), before);
    }
    let conn = Connection::open(&path).unwrap();
    assert_eq!(
        conn.query_row("SELECT count(*) FROM s1_requests", [], |r| r
            .get::<_, i64>(0))
            .unwrap(),
        1
    );
    drop(conn);
    create(
        &path,
        "project",
        None,
        "Valid retry after rejected payload",
        "invalid",
    );
}

#[test]
fn s1_soft_delete_nonempty_guards_and_deleted_parent() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    let p = create(&path, "project", None, "P", "1");
    let t = create(&path, "theme", Some(&p), "T", "2");
    let a = create(&path, "assay", Some(&t), "A", "3");
    assert_eq!(
        execute_at(
            &path,
            request("delete", Some("project"), Some(&p), None, None, Some("dp"))
        )
        .unwrap_err()
        .code,
        "NODE_IN_USE"
    );
    assert_eq!(
        execute_at(
            &path,
            request("delete", Some("theme"), Some(&t), None, None, Some("dt"))
        )
        .unwrap_err()
        .code,
        "NODE_IN_USE"
    );
    execute_at(
        &path,
        request("delete", Some("assay"), Some(&a), None, None, Some("da")),
    )
    .unwrap();
    execute_at(
        &path,
        request("delete", Some("theme"), Some(&t), None, None, Some("dt")),
    )
    .unwrap();
    assert!(execute_at(
        &path,
        request(
            "create",
            Some("assay"),
            None,
            Some(&t),
            Some("orphan"),
            Some("orphan")
        )
    )
    .is_err());
    execute_at(
        &path,
        request("delete", Some("project"), Some(&p), None, None, Some("dp")),
    )
    .unwrap();
    assert!(list(&path).nodes.is_empty());
    let conn = Connection::open(&path).unwrap();
    assert_eq!(
        conn.query_row(
            "SELECT count(*) FROM assays WHERE deleted_at IS NOT NULL",
            [],
            |r| r.get::<_, i64>(0)
        )
        .unwrap(),
        1
    );
}

#[test]
fn s1_replay_is_idempotent_and_payload_reuse_conflicts() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    let original = request(
        "create",
        Some("project"),
        None,
        None,
        Some("P"),
        Some("token"),
    );
    let first = execute_at(&path, original.clone()).unwrap();
    assert_eq!(execute_at(&path, original).unwrap(), first);
    assert_eq!(
        execute_at(
            &path,
            request(
                "create",
                Some("project"),
                None,
                None,
                Some("Q"),
                Some("token")
            )
        )
        .unwrap_err()
        .code,
        "REQUEST_CONFLICT"
    );
    assert_eq!(list(&path), first);
}

#[test]
fn s1_unknown_databases_are_byte_identical_and_never_initialized() {
    let dir = tempfile::tempdir().unwrap();
    for (index, sql) in [
        "",
        "CREATE TABLE private_data(value TEXT); INSERT INTO private_data VALUES('untouched');",
        "PRAGMA user_version=99;",
        "CREATE TABLE schema_migrations(version INTEGER); INSERT INTO schema_migrations VALUES(1);",
    ]
    .iter()
    .enumerate()
    {
        let path = dir.path().join(format!("unknown-{index}.sqlite"));
        let conn = Connection::open(&path).unwrap();
        conn.execute_batch(sql).unwrap();
        drop(conn);
        let bytes = std::fs::read(&path).unwrap();
        let modified = std::fs::metadata(&path).unwrap().modified().unwrap();
        assert_eq!(
            execute_at(&path, request("list", None, None, None, None, None))
                .unwrap_err()
                .code,
            "UNKNOWN_DATABASE"
        );
        assert_eq!(std::fs::read(&path).unwrap(), bytes);
        assert_eq!(
            std::fs::metadata(&path).unwrap().modified().unwrap(),
            modified
        );
        assert!(!path.with_extension("sqlite-journal").exists());
    }
}

#[test]
fn s1_tampered_owned_schema_and_history_are_rejected_without_writes() {
    let dir = tempfile::tempdir().unwrap();
    for (index, sql) in [
        "PRAGMA application_id=0;",
        "PRAGMA user_version=4;",
        "DELETE FROM schema_migrations;",
        "ALTER TABLE projects ADD COLUMN foreign_data TEXT;",
    ]
    .iter()
    .enumerate()
    {
        let path = dir.path().join(format!("tampered-{index}.sqlite"));
        create(&path, "project", None, "P", "1");
        let conn = Connection::open(&path).unwrap();
        conn.execute_batch(sql).unwrap();
        drop(conn);
        let bytes = std::fs::read(&path).unwrap();
        let modified = std::fs::metadata(&path).unwrap().modified().unwrap();
        assert_eq!(
            execute_at(&path, request("list", None, None, None, None, None))
                .unwrap_err()
                .code,
            "UNKNOWN_DATABASE"
        );
        assert_eq!(std::fs::read(&path).unwrap(), bytes);
        assert_eq!(
            std::fs::metadata(&path).unwrap().modified().unwrap(),
            modified
        );
    }
}

#[test]
fn s1_invalid_request_shape_never_creates_storage_or_leaks_input() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("not-created").join("tree.sqlite");
    for bad in [
        request(
            "create",
            Some("../../projects"),
            None,
            None,
            Some("P"),
            Some("1"),
        ),
        request("list", Some("project"), None, None, None, None),
        request("rename", Some("project"), None, None, Some("P"), Some("1")),
        request(
            "delete",
            Some("project"),
            Some("id"),
            None,
            Some("unexpected"),
            Some("1"),
        ),
        request("create", Some("theme"), None, None, Some("T"), Some("1")),
        request(
            "create",
            Some("project"),
            Some("client-id"),
            None,
            Some("P"),
            Some("1"),
        ),
        request(
            "create",
            Some("project"),
            None,
            None,
            Some("bad\0name"),
            Some("1"),
        ),
    ] {
        let error = execute_at(&path, bad).unwrap_err();
        let serialized = serde_json::to_string(&error).unwrap();
        assert!(!serialized.contains("../../projects"));
        assert!(!serialized.contains("tree.sqlite"));
        assert!(!path.exists());
        assert!(!path.parent().unwrap().exists());
    }
    assert!(serde_json::from_value::<TreeRequest>(
        serde_json::json!({"action":"list","databasePath":"C:/private.sqlite"})
    )
    .is_err());
}

#[test]
fn s1_unicode_workspace_path_and_retry_return_current_database_snapshot() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("项目 # ? %").join("tree.sqlite");
    // '?' is not a valid Windows filename; use it only on platforms that allow it.
    #[cfg(windows)]
    let path = dir.path().join("项目 # %").join("tree.sqlite");
    let original = request(
        "create",
        Some("project"),
        None,
        None,
        Some(" P 🧪 "),
        Some("1"),
    );
    let created = execute_at(&path, original.clone()).unwrap();
    assert_eq!(created.nodes[0].name, "P 🧪");
    let id = &created.nodes[0].id;
    let rename = request(
        "rename",
        Some("project"),
        Some(id),
        None,
        Some("Renamed"),
        Some("2"),
    );
    let renamed = execute_at(&path, rename.clone()).unwrap();
    assert_eq!(execute_at(&path, original).unwrap(), renamed);
    assert_eq!(execute_at(&path, rename).unwrap(), renamed);
    let delete = request("delete", Some("project"), Some(id), None, None, Some("3"));
    assert!(execute_at(&path, delete.clone()).unwrap().nodes.is_empty());
    assert!(execute_at(&path, delete).unwrap().nodes.is_empty());
}

#[test]
fn s1_asset_and_group_associations_prevent_assay_deletion() {
    let dir = tempfile::tempdir().unwrap();
    let path = dir.path().join("tree.sqlite");
    let p = create(&path, "project", None, "P", "1");
    let t = create(&path, "theme", Some(&p), "T", "2");
    let a = create(&path, "assay", Some(&t), "A", "3");
    let conn = Connection::open(&path).unwrap();
    conn.execute(
        "INSERT INTO data_assets(id,name) VALUES('asset','File')",
        [],
    )
    .unwrap();
    conn.execute("INSERT INTO asset_links(id,asset_id,assay_id,stage,display_name) VALUES('link','asset',?1,'raw','File')",[&a]).unwrap();
    drop(conn);
    assert_eq!(
        execute_at(
            &path,
            request(
                "delete",
                Some("assay"),
                Some(&a),
                None,
                None,
                Some("delete")
            )
        )
        .unwrap_err()
        .code,
        "NODE_IN_USE"
    );
    let conn = Connection::open(&path).unwrap();
    conn.execute("UPDATE asset_links SET deleted_at='deleted'", [])
        .unwrap();
    conn.execute(
        "INSERT INTO experiment_groups(id,assay_id,name) VALUES('g',?1,'Group')",
        [&a],
    )
    .unwrap();
    drop(conn);
    assert_eq!(
        execute_at(
            &path,
            request(
                "delete",
                Some("assay"),
                Some(&a),
                None,
                None,
                Some("delete")
            )
        )
        .unwrap_err()
        .code,
        "NODE_IN_USE"
    );
}
