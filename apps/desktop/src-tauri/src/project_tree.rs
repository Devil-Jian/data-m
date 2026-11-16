//! S1 disposable workspace only. No startup hook or user-selected paths.
use rusqlite::{params, Connection, OpenFlags, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::{path::Path, sync::Mutex, time::Duration};
use tauri::Manager;

const APPLICATION_ID: i64 = 0x444d5331;
pub(super) const MIGRATION: &str = include_str!("../migrations/0001_initial.sql");
const MIGRATION_2: &str = include_str!("../migrations/0002_import_jobs.sql");
pub(super) const REQUEST_SCHEMA: &str =
    "CREATE TABLE s1_requests(request_id TEXT PRIMARY KEY, payload TEXT NOT NULL) STRICT;";
static ACCESS: Mutex<()> = Mutex::new(());

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct TreeRequest {
    action: String,
    kind: Option<String>,
    id: Option<String>,
    parent_id: Option<String>,
    name: Option<String>,
    request_id: Option<String>,
}
#[derive(Debug, Clone, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct TreeNode {
    id: String,
    kind: String,
    parent_id: Option<String>,
    name: String,
}
#[derive(Debug, Serialize, PartialEq, Eq)]
pub struct TreeSnapshot {
    nodes: Vec<TreeNode>,
}
#[derive(Debug, Serialize)]
pub struct TreeError {
    pub code: &'static str,
    pub message: &'static str,
}
impl TreeError {
    pub(super) fn new(code: &'static str, message: &'static str) -> Self {
        Self { code, message }
    }
    pub(super) fn input() -> Self {
        Self::new("INVALID_REQUEST", "The project tree request is invalid.")
    }
    fn unknown() -> Self {
        Self::new(
            "UNKNOWN_DATABASE",
            "This database is not a recognized S1 test workspace.",
        )
    }
}
impl From<rusqlite::Error> for TreeError {
    fn from(_: rusqlite::Error) -> Self {
        Self::new(
            "DATABASE_UNAVAILABLE",
            "The project tree could not be saved or loaded. Please retry.",
        )
    }
}

#[tauri::command]
pub fn project_tree<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: TreeRequest,
) -> Result<TreeSnapshot, TreeError> {
    let path = database_path(&app)?;
    execute_at(&path, request)
}

pub(super) fn database_path<R: tauri::Runtime>(
    app: &tauri::AppHandle<R>,
) -> Result<std::path::PathBuf, TreeError> {
    #[cfg(test)]
    if let Some(path) = app.try_state::<TestDatabasePath>() {
        return Ok(path.0.clone());
    }
    let path = app
        .path()
        .app_local_data_dir()
        .map_err(|_| TreeError::new("DATABASE_UNAVAILABLE", "The test workspace is unavailable."))?
        .join("s1-test-workspace")
        .join("project-tree.sqlite");
    Ok(path)
}

#[cfg(test)]
pub(super) struct TestDatabasePath(pub std::path::PathBuf);

// No UI value is interpolated into SQL. These identifiers are exhaustive constants.
#[derive(Clone, Copy)]
enum Kind {
    Project,
    Theme,
    Assay,
}
impl Kind {
    fn parse(value: Option<&str>) -> Result<Self, TreeError> {
        match value {
            Some("project") => Ok(Self::Project),
            Some("theme") => Ok(Self::Theme),
            Some("assay") => Ok(Self::Assay),
            _ => Err(TreeError::input()),
        }
    }
    fn table(self) -> &'static str {
        match self {
            Self::Project => "projects",
            Self::Theme => "themes",
            Self::Assay => "assays",
        }
    }
    fn parent(self) -> Option<(&'static str, &'static str)> {
        match self {
            Self::Project => None,
            Self::Theme => Some(("projects", "project_id")),
            Self::Assay => Some(("themes", "theme_id")),
        }
    }
}
fn valid_text(value: &str, max: usize) -> bool {
    !value.trim().is_empty() && value.chars().count() <= max && !value.chars().any(|c| c.is_control() || matches!(c, '\u{200b}'..='\u{200f}' | '\u{202a}'..='\u{202e}' | '\u{2060}'..='\u{206f}' | '\u{feff}'))
}
pub(super) fn required(value: Option<&str>, max: usize) -> Result<&str, TreeError> {
    value
        .filter(|s| valid_text(s, max))
        .ok_or_else(TreeError::input)
}
pub(super) fn required_name(value: Option<&str>) -> Result<&str, TreeError> {
    value.filter(|s| valid_text(s, 255)).ok_or_else(|| {
        TreeError::new(
            "INVALID_NAME",
            "Use a name of 1 to 255 characters without control characters.",
        )
    })
}
fn validate(request: &TreeRequest) -> Result<(), TreeError> {
    if request.action == "list" {
        return if request.kind.is_none()
            && request.id.is_none()
            && request.parent_id.is_none()
            && request.name.is_none()
            && request.request_id.is_none()
        {
            Ok(())
        } else {
            Err(TreeError::input())
        };
    }
    let kind = Kind::parse(request.kind.as_deref())?;
    required(request.request_id.as_deref(), 128)?;
    match request.action.as_str() {
        "create" => {
            if request.id.is_some() {
                return Err(TreeError::input());
            }
            required_name(request.name.as_deref())?;
            if kind.parent().is_some() {
                required(request.parent_id.as_deref(), 128)?;
            } else if request.parent_id.is_some() {
                return Err(TreeError::input());
            }
        }
        "rename" | "delete" => {
            required(request.id.as_deref(), 128)?;
            if request.parent_id.is_some() {
                return Err(TreeError::input());
            }
            if request.action == "rename" {
                required_name(request.name.as_deref())?;
            } else if request.name.is_some() {
                return Err(TreeError::input());
            }
        }
        _ => return Err(TreeError::input()),
    }
    Ok(())
}
fn schema(connection: &Connection) -> rusqlite::Result<Vec<(String, String, String)>> {
    let mut statement = connection.prepare(
        "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name",
    )?;
    let rows = statement.query_map([], |r| {
        let sql: String = r.get(2)?;
        Ok((r.get(0)?, r.get(1)?, sql.replace("\r\n", "\n")))
    })?;
    rows.collect()
}
fn initialize(connection: &mut Connection) -> Result<(), TreeError> {
    let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
    tx.execute_batch(MIGRATION)?;
    tx.execute_batch(REQUEST_SCHEMA)?;
    tx.pragma_update(None, "application_id", APPLICATION_ID)?;
    tx.pragma_update(None, "user_version", 1)?;
    tx.commit()?;
    Ok(())
}
pub(super) fn recognized(connection: &Connection) -> Result<(), TreeError> {
    let result = (|| -> rusqlite::Result<bool> {
        let application: i64 =
            connection.pragma_query_value(None, "application_id", |r| r.get(0))?;
        let version: i64 = connection.pragma_query_value(None, "user_version", |r| r.get(0))?;
        if application != APPLICATION_ID || !matches!(version, 1 | 2) {
            return Ok(false);
        }
        let expected = Connection::open_in_memory()?;
        expected.execute_batch(MIGRATION)?;
        expected.execute_batch(REQUEST_SCHEMA)?;
        if version == 2 {
            expected.execute_batch(MIGRATION_2)?;
        }
        if schema(connection)? != schema(&expected)? {
            return Ok(false);
        }
        let history: (i64, i64, i64) = connection.query_row(
            "SELECT count(*),min(version),max(version) FROM schema_migrations",
            [],
            |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?)),
        )?;
        Ok(history == (version, 1, version))
    })();
    match result {
        Ok(true) => Ok(()),
        _ => Err(TreeError::unknown()),
    }
}
// Publish only a complete database. persist_noclobber never replaces an existing
// target, including one published by another process during initialization.
fn publish_new_database(
    path: &Path,
    initializer: impl FnOnce(&mut Connection) -> Result<(), TreeError>,
) -> Result<(), TreeError> {
    let parent = path.parent().ok_or_else(TreeError::input)?;
    let staging = tempfile::Builder::new()
        .prefix(".s1-initializing-")
        .tempfile_in(parent)
        .map_err(|_| {
            TreeError::new("DATABASE_UNAVAILABLE", "The test workspace is unavailable.")
        })?;
    let mut connection =
        Connection::open_with_flags(staging.path(), OpenFlags::SQLITE_OPEN_READ_WRITE)?;
    initializer(&mut connection)?;
    connection
        .close()
        .map_err(|(_, error)| TreeError::from(error))?;
    match staging.persist_noclobber(path) {
        Ok(file) => {
            drop(file);
            Ok(())
        }
        Err(error) if error.error.kind() == std::io::ErrorKind::AlreadyExists => Ok(()),
        Err(_) => Err(TreeError::new(
            "DATABASE_UNAVAILABLE",
            "The test workspace is unavailable.",
        )),
    }
}
fn open(path: &Path) -> Result<Connection, TreeError> {
    let parent = path.parent().ok_or_else(TreeError::input)?;
    std::fs::create_dir_all(parent).map_err(|_| {
        TreeError::new("DATABASE_UNAVAILABLE", "The test workspace is unavailable.")
    })?;
    if !path.exists() {
        publish_new_database(path, initialize)?;
    }
    validate_readonly(path)?;
    let mut connection = Connection::open_with_flags(path, OpenFlags::SQLITE_OPEN_READ_WRITE)?;
    connection.busy_timeout(Duration::from_secs(5))?;
    // A second validation prevents using a replaced or partially initialized file.
    recognized(&connection)?;
    connection.pragma_update(None, "foreign_keys", "ON")?;
    // Upgrade only a branded, fully recognized v1 repository. The transaction
    // preserves every existing identifier and rolls back schema/history together.
    if connection.pragma_query_value(None, "user_version", |row| row.get::<_, i64>(0))? == 1 {
        let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
        // Another process may have upgraded while this connection waited.
        recognized(&tx)?;
        if tx.pragma_query_value(None, "user_version", |row| row.get::<_, i64>(0))? == 1 {
            tx.execute_batch(MIGRATION_2)?;
            tx.pragma_update(None, "user_version", 2)?;
        }
        tx.commit()?;
    }
    Ok(connection)
}

pub(super) fn validate_readonly(path: &Path) -> Result<(), TreeError> {
        // immutable disables journaling/recovery and prevents sidecar creation during
        // recognition, even for an unfamiliar WAL database. Ours uses DELETE journals.
        let absolute = std::fs::canonicalize(path).map_err(|_| TreeError::unknown())?;
        let absolute_text = absolute.to_string_lossy();
        let path_text = absolute_text
            .strip_prefix(r"\\?\")
            .unwrap_or(&absolute_text)
            .replace('\\', "/");
        let encoded: String = path_text
            .bytes()
            .map(|b| {
                if b.is_ascii_alphanumeric() || b"/:_-.".contains(&b) {
                    (b as char).to_string()
                } else {
                    format!("%{b:02X}")
                }
            })
            .collect();
        let readonly = Connection::open_with_flags(
            format!("file:{encoded}?immutable=1"),
            OpenFlags::SQLITE_OPEN_READ_ONLY | OpenFlags::SQLITE_OPEN_URI,
        )
        .map_err(|_| TreeError::unknown())?;
        recognized(&readonly)
}
fn execute_at(path: &Path, request: TreeRequest) -> Result<TreeSnapshot, TreeError> {
    validate(&request)?;
    with_database_at(path, |connection| execute_request(connection, request))
}

pub(super) fn with_database_at<T>(
    path: &Path,
    operation: impl FnOnce(&mut Connection) -> Result<T, TreeError>,
) -> Result<T, TreeError> {
    let _guard = ACCESS.lock().map_err(|_| {
        TreeError::new(
            "DATABASE_UNAVAILABLE",
            "The test workspace is busy. Please restart the application.",
        )
    })?;
    let mut connection = open(path)?;
    operation(&mut connection)
}

fn execute_request(
    connection: &mut Connection,
    request: TreeRequest,
) -> Result<TreeSnapshot, TreeError> {
    if request.action != "list" {
        let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
        let token = request.request_id.as_deref().ok_or_else(TreeError::input)?;
        let payload = serde_json::to_string(&request).map_err(|_| TreeError::input())?;
        let previous: Option<String> = tx
            .query_row(
                "SELECT payload FROM s1_requests WHERE request_id=?1",
                [token],
                |r| r.get(0),
            )
            .optional()?;
        match previous {
            Some(previous) if previous != payload => {
                return Err(TreeError::new(
                    "REQUEST_CONFLICT",
                    "This request ID was already used for a different operation.",
                ))
            }
            Some(_) => {}
            None => {
                mutate(&tx, &request)?;
                tx.execute(
                    "INSERT INTO s1_requests(request_id,payload) VALUES(?1,?2)",
                    params![token, payload],
                )?;
            }
        }
        tx.commit()?;
    }
    snapshot(connection)
}
fn active(connection: &Connection, table: &str, id: &str) -> Result<bool, TreeError> {
    Ok(connection.query_row(
        &format!("SELECT EXISTS(SELECT 1 FROM {table} WHERE id=?1 AND deleted_at IS NULL)"),
        [id],
        |r| r.get(0),
    )?)
}
fn mutate(connection: &Connection, request: &TreeRequest) -> Result<(), TreeError> {
    let kind = Kind::parse(request.kind.as_deref())?;
    let table = kind.table();
    if request.action == "create" {
        let name = required_name(request.name.as_deref())?.trim();
        if let Some((parent_table, parent_column)) = kind.parent() {
            let parent = required(request.parent_id.as_deref(), 128)?;
            if !active(connection, parent_table, parent)? {
                return Err(TreeError::new(
                    "INVALID_PARENT",
                    "Select an existing parent for this item.",
                ));
            }
            connection.execute(&format!("INSERT INTO {table}(id,{parent_column},name) VALUES(lower(hex(randomblob(16))),?1,?2)"),params![parent,name])?;
        } else {
            connection.execute(
                "INSERT INTO projects(id,name) VALUES(lower(hex(randomblob(16))),?1)",
                [name],
            )?;
        }
        return Ok(());
    }
    let id = required(request.id.as_deref(), 128)?;
    if !active(connection, table, id)? {
        return Err(TreeError::new(
            "NOT_FOUND",
            "The selected item no longer exists.",
        ));
    }
    if request.action == "rename" {
        connection.execute(
            &format!("UPDATE {table} SET name=?1 WHERE id=?2"),
            params![required_name(request.name.as_deref())?.trim(), id],
        )?;
    } else {
        let guard=match kind {
            Kind::Project=>"SELECT EXISTS(SELECT 1 FROM themes WHERE project_id=?1 AND deleted_at IS NULL UNION ALL SELECT 1 FROM papers WHERE project_id=?1 AND deleted_at IS NULL)",
            Kind::Theme=>"SELECT EXISTS(SELECT 1 FROM assays WHERE theme_id=?1 AND deleted_at IS NULL)",
            Kind::Assay=>"SELECT EXISTS(SELECT 1 FROM experiment_groups WHERE assay_id=?1 AND deleted_at IS NULL UNION ALL SELECT 1 FROM asset_links WHERE assay_id=?1 AND deleted_at IS NULL UNION ALL SELECT 1 FROM statistical_analyses WHERE assay_id=?1)",
        };
        if connection.query_row(guard, [id], |r| r.get::<_, bool>(0))? {
            return Err(TreeError::new(
                "NODE_IN_USE",
                "Remove this item's children and associations before deleting it.",
            ));
        }
        connection.execute(
            &format!(
                "UPDATE {table} SET deleted_at=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE id=?1"
            ),
            [id],
        )?;
    }
    Ok(())
}
fn snapshot(connection: &Connection) -> Result<TreeSnapshot, TreeError> {
    let mut statement=connection.prepare("SELECT id,'project' AS kind,NULL AS parent_id,name FROM projects WHERE deleted_at IS NULL UNION ALL SELECT t.id,'theme',t.project_id,t.name FROM themes t JOIN projects p ON p.id=t.project_id WHERE t.deleted_at IS NULL AND p.deleted_at IS NULL UNION ALL SELECT a.id,'assay',a.theme_id,a.name FROM assays a JOIN themes t ON t.id=a.theme_id JOIN projects p ON p.id=t.project_id WHERE a.deleted_at IS NULL AND t.deleted_at IS NULL AND p.deleted_at IS NULL ORDER BY kind,parent_id,name,id")?;
    let nodes = statement
        .query_map([], |r| {
            Ok(TreeNode {
                id: r.get(0)?,
                kind: r.get(1)?,
                parent_id: r.get(2)?,
                name: r.get(3)?,
            })
        })?
        .collect::<rusqlite::Result<Vec<_>>>()?;
    Ok(TreeSnapshot { nodes })
}

#[cfg(test)]
#[path = "project_tree_tests.rs"]
mod tests;
