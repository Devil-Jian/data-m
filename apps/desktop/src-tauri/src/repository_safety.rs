//! S7 disposable repository safety: explicit read-only audits, WAL-safe snapshots,
//! and recovery into an isolated, previously nonexistent test directory.
//! No startup side effects, user-supplied filesystem paths, or automatic repairs.
use crate::project_tree::{database_path, recognized, TreeError};
use rusqlite::{Connection, OpenFlags, OptionalExtension};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::{BTreeMap, BTreeSet},
    fs::{self, File, OpenOptions},
    io::{self, Read, Write},
    path::{Component, Path, PathBuf},
    time::Duration,
};
use tauri::{Emitter, Manager};

mod snapshot;
#[cfg(test)]
mod tests;

const TABLES: &[&str] = &[
    "schema_migrations",
    "projects",
    "themes",
    "assays",
    "experiment_groups",
    "replicates",
    "blobs",
    "data_assets",
    "asset_versions",
    "asset_links",
    "statistical_analyses",
    "analysis_samples",
    "papers",
    "figures",
    "panels",
    "panel_asset_versions",
    "representative_sources",
    "audit_events",
    "s1_requests",
    "import_jobs",
    "import_files",
];
const S8_TABLES: &[&str] = &["analysis_asset_versions"];
const MAX_ISSUES: usize = 100;
const CHUNK: usize = 1024 * 1024;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SafetyRequest {
    action: String,
    snapshot_id: Option<String>,
    restore_label: Option<String>,
    confirm: Option<bool>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Issue {
    code: String,
    subject: String,
}
#[derive(Debug, Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SafetyReport {
    checked_blobs: usize,
    verified_blobs: usize,
    issues: Vec<Issue>,
    truncated: bool,
    table_counts: BTreeMap<String, i64>,
}
impl SafetyReport {
    fn push(&mut self, code: &str, subject: &str) {
        if self.issues.len() >= MAX_ISSUES {
            self.truncated = true;
        } else {
            self.issues.push(Issue {
                code: code.into(),
                subject: subject.into(),
            });
        }
    }
    fn clean(&self) -> bool {
        self.issues.is_empty() && !self.truncated
    }
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SnapshotSummary {
    snapshot_id: String,
    blob_count: usize,
    version_count: usize,
    schema_version: i64,
    created_at: String,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SafetyResponse {
    action: String,
    status: String,
    snapshot_id: Option<String>,
    restore_label: Option<String>,
    report: Option<SafetyReport>,
    snapshots: Vec<SnapshotSummary>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SafetyProgress {
    phase: String,
    completed: usize,
    total: usize,
}
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct BlobRecord {
    sha256: String,
    size_bytes: i64,
}
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct VersionRecord {
    id: String,
    asset_id: String,
    version_number: i64,
    blob_sha256: String,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct Manifest {
    format_version: i64,
    snapshot_id: String,
    schema_version: i64,
    database_sha256: String,
    database_size_bytes: u64,
    created_at: String,
    table_counts: BTreeMap<String, i64>,
    blobs: Vec<BlobRecord>,
    versions: Vec<VersionRecord>,
}
fn failure(code: &'static str) -> TreeError {
    let message = match code {
        "INVALID_REQUEST" => "The safety request or restore label is invalid.",
        "UNKNOWN_DATABASE" => {
            "Only an existing, recognized test repository can be audited or backed up."
        }
        "UNSAFE_PATH" => "A folder or file is missing, linked or unsafe. No changes were made.",
        "BACKUP_BUSY" => "Finish or cancel pending imports before creating a complete snapshot.",
        "INTEGRITY_FAILED" => "Integrity problems were detected; snapshot or restore was refused.",
        "SNAPSHOT_NOT_FOUND" => "This immutable test snapshot is not available.",
        "SNAPSHOT_CORRUPT" => "The snapshot manifest, database or files failed verification.",
        "RESTORE_CONFLICT" => "The target restore label already exists. No files were overwritten.",
        "DISK_WRITE_FAILED" => {
            "The snapshot or restore could not be written. Existing data was preserved."
        }
        _ => "The repository safety operation was not completed.",
    };
    TreeError::new(code, message)
}
fn map_read(_: impl std::fmt::Debug) -> TreeError {
    failure("INTEGRITY_FAILED")
}
fn map_write(_: impl std::fmt::Debug) -> TreeError {
    failure("DISK_WRITE_FAILED")
}
fn valid_id(s: &str) -> bool {
    s.len() == 32
        && s.bytes()
            .all(|c| c.is_ascii_digit() || (b'a'..=b'f').contains(&c))
}
fn valid_label(s: &str) -> bool {
    !s.is_empty()
        && s.len() <= 50
        && s.bytes()
            .all(|c| c.is_ascii_alphanumeric() || c == b'-' || c == b'_')
        && !matches!(
            s.to_ascii_lowercase().as_str(),
            "con" | "nul" | "aux" | "prn"
        )
        && !s.to_ascii_lowercase().starts_with("com")
        && !s.to_ascii_lowercase().starts_with("lpt")
}
fn validate(req: &SafetyRequest) -> Result<(), TreeError> {
    match req.action.as_str() {
        "audit" | "list" | "backup"
            if req.snapshot_id.is_none()
                && req.restore_label.is_none()
                && req.confirm.is_none() =>
        {
            Ok(())
        }
        "verify"
            if req.snapshot_id.as_deref().is_some_and(valid_id)
                && req.restore_label.is_none()
                && req.confirm.is_none() =>
        {
            Ok(())
        }
        "restore"
            if req.snapshot_id.as_deref().is_some_and(valid_id)
                && req.restore_label.as_deref().is_some_and(valid_label)
                && req.confirm == Some(true) =>
        {
            Ok(())
        }
        _ => Err(failure("INVALID_REQUEST")),
    }
}
fn regular(meta: &fs::Metadata, directory: bool) -> bool {
    if meta.file_type().is_symlink() || meta.is_dir() != directory || meta.is_file() == directory {
        return false;
    }
    #[cfg(windows)]
    {
        use std::os::windows::fs::MetadataExt;
        if meta.file_attributes() & 0x400 != 0 {
            return false;
        }
    }
    true
}
// Examine every ancestor; do not canonicalize away a reparse point before checking.
fn safe_directory(path: &Path, create: bool) -> Result<(), TreeError> {
    if !path.is_absolute()
        || path
            .components()
            .any(|c| matches!(c, Component::ParentDir | Component::CurDir))
    {
        return Err(failure("UNSAFE_PATH"));
    }
    if !path.exists() {
        if !create {
            return Err(failure("UNSAFE_PATH"));
        }
        let parent = path.parent().ok_or_else(|| failure("UNSAFE_PATH"))?;
        if parent != path {
            safe_directory(parent, true)?;
        }
        fs::create_dir(path).map_err(map_write)?;
    } else if let Some(parent) = path.parent() {
        if parent != path {
            safe_directory(parent, false)?;
        }
    }
    let meta = fs::symlink_metadata(path).map_err(map_read)?;
    if !regular(&meta, true) {
        return Err(failure("UNSAFE_PATH"));
    }
    Ok(())
}
fn safe_regular(path: &Path) -> Result<(), TreeError> {
    safe_directory(path.parent().ok_or_else(|| failure("UNSAFE_PATH"))?, false)?;
    let meta = fs::symlink_metadata(path).map_err(map_read)?;
    if !regular(&meta, false) {
        return Err(failure("UNSAFE_PATH"));
    }
    Ok(())
}
fn safe_optional_dir(path: &Path) -> Result<bool, TreeError> {
    match fs::symlink_metadata(path) {
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(false),
        Err(_) => Err(failure("UNSAFE_PATH")),
        Ok(meta) => {
            if !regular(&meta, true) {
                return Err(failure("UNSAFE_PATH"));
            }
            Ok(true)
        }
    }
}
fn sha256_file(path: &Path) -> Result<(String, u64), TreeError> {
    safe_regular(path)?;
    let mut file = File::open(path).map_err(map_read)?;
    let mut digest = Sha256::new();
    let mut length = 0_u64;
    let mut block = vec![0_u8; CHUNK];
    loop {
        let n = file.read(&mut block).map_err(map_read)?;
        if n == 0 {
            break;
        }
        digest.update(&block[..n]);
        length = length
            .checked_add(n as u64)
            .ok_or_else(|| failure("INTEGRITY_FAILED"))?;
    }
    Ok((format!("{:x}", digest.finalize()), length))
}
fn copy_verified(src: &Path, dest: &Path, sha: &str, expected: u64) -> Result<(), TreeError> {
    let (h, len) = sha256_file(src)?;
    if h != sha || len != expected {
        return Err(failure("INTEGRITY_FAILED"));
    }
    let mut input = File::open(src).map_err(map_read)?;
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(dest)
        .map_err(map_write)?;
    let mut digest = Sha256::new();
    let mut count = 0_u64;
    let mut bytes = vec![0_u8; CHUNK];
    loop {
        let n = input.read(&mut bytes).map_err(map_read)?;
        if n == 0 {
            break;
        }
        output.write_all(&bytes[..n]).map_err(map_write)?;
        digest.update(&bytes[..n]);
        count += n as u64;
    }
    output.sync_all().map_err(map_write)?;
    if count != expected || format!("{:x}", digest.finalize()) != sha {
        return Err(failure("INTEGRITY_FAILED"));
    }
    let (check, size) = sha256_file(dest)?;
    if check != sha || size != expected {
        return Err(failure("INTEGRITY_FAILED"));
    }
    Ok(())
}
fn open_recognized(path: &Path) -> Result<Connection, TreeError> {
    if !path.is_file() {
        return Err(failure("UNKNOWN_DATABASE"));
    }
    safe_regular(path)?;
    let c = Connection::open_with_flags(path, OpenFlags::SQLITE_OPEN_READ_ONLY)
        .map_err(|_| failure("UNKNOWN_DATABASE"))?;
    c.busy_timeout(Duration::from_secs(5)).map_err(map_read)?;
    recognized(&c).map_err(|_| failure("UNKNOWN_DATABASE"))?;
    if c.pragma_query_value(None, "user_version", |row| row.get::<_, i64>(0))
        .map_err(map_read)?
        != 2 && c.pragma_query_value(None, "user_version", |row| row.get::<_, i64>(0))
            .map_err(map_read)? != 3
    {
        return Err(failure("UNKNOWN_DATABASE"));
    }
    Ok(c)
}
fn counts(c: &Connection) -> Result<BTreeMap<String, i64>, TreeError> {
    let mut result = BTreeMap::new();
    let version: i64 = c.pragma_query_value(None, "user_version", |row| row.get(0)).map_err(map_read)?;
    for table in TABLES.iter().copied().chain(if version >= 3 { S8_TABLES.iter().copied() } else { [].iter().copied() }) {
        let count: i64 = c
            .query_row(&format!("SELECT count(*) FROM {table}"), [], |r| r.get(0))
            .map_err(map_read)?;
        result.insert(table.into(), count);
    }
    Ok(result)
}
fn blob_list(c: &Connection) -> Result<Vec<BlobRecord>, TreeError> {
    let mut stmt = c
        .prepare("SELECT sha256,size_bytes FROM blobs ORDER BY sha256")
        .map_err(map_read)?;
    let result = stmt
        .query_map([], |r| {
            Ok(BlobRecord {
                sha256: r.get(0)?,
                size_bytes: r.get(1)?,
            })
        })
        .map_err(map_read)?
        .collect::<rusqlite::Result<Vec<_>>>()
        .map_err(map_read);
    result
}
fn version_list(c: &Connection) -> Result<Vec<VersionRecord>, TreeError> {
    let mut stmt = c
        .prepare("SELECT id,asset_id,version_number,blob_sha256 FROM asset_versions ORDER BY id")
        .map_err(map_read)?;
    let result = stmt
        .query_map([], |r| {
            Ok(VersionRecord {
                id: r.get(0)?,
                asset_id: r.get(1)?,
                version_number: r.get(2)?,
                blob_sha256: r.get(3)?,
            })
        })
        .map_err(map_read)?
        .collect::<rusqlite::Result<Vec<_>>>()
        .map_err(map_read);
    result
}
fn database_issues(c: &Connection, report: &mut SafetyReport) -> Result<(), TreeError> {
    let mut stmt = c.prepare("PRAGMA integrity_check").map_err(map_read)?;
    let rows = stmt
        .query_map([], |r| r.get::<_, String>(0))
        .map_err(map_read)?;
    for row in rows {
        if row.map_err(map_read)? != "ok" {
            report.push("SQLITE_INTEGRITY", "database");
        }
    }
    let mut stmt = c.prepare("PRAGMA foreign_key_check").map_err(map_read)?;
    let mut rows = stmt.query([]).map_err(map_read)?;
    while rows.next().map_err(map_read)?.is_some() {
        report.push("FOREIGN_KEY", "database");
    }
    Ok(())
}
fn inspect_objects(
    root: &Path,
    staging: &Path,
    registered: &[BlobRecord],
    c: &Connection,
    report: &mut SafetyReport,
) -> Result<(), TreeError> {
    let names = registered
        .iter()
        .map(|b| b.sha256.as_str())
        .collect::<BTreeSet<_>>();
    if safe_optional_dir(root)? {
        safe_directory(root, false)?;
        for entry in fs::read_dir(root).map_err(map_read)? {
            let entry = entry.map_err(map_read)?;
            let name = entry.file_name().to_string_lossy().into_owned();
            if name != ".data-m-directory-pin" && !names.contains(name.as_str()) {
                report.push("ORPHAN_OBJECT", &name.chars().take(64).collect::<String>());
            }
        }
    }
    if safe_optional_dir(staging)? {
        safe_directory(staging, false)?;
        let mut known = BTreeSet::new();
        let mut stmt = c
            .prepare("SELECT staging_name FROM import_files")
            .map_err(map_read)?;
        for item in stmt
            .query_map([], |r| r.get::<_, String>(0))
            .map_err(map_read)?
        {
            known.insert(item.map_err(map_read)?);
        }
        for entry in fs::read_dir(staging).map_err(map_read)? {
            let entry = entry.map_err(map_read)?;
            let name = entry.file_name().to_string_lossy().into_owned();
            if name != ".data-m-directory-pin" && !known.contains(&name) {
                report.push("ORPHAN_STAGING", &name.chars().take(64).collect::<String>());
            }
        }
    }
    Ok(())
}
fn inspect(c: &Connection, cas_root: &Path) -> Result<SafetyReport, TreeError> {
    let mut report = SafetyReport::default();
    report.table_counts = counts(c)?;
    database_issues(c, &mut report)?;
    let registered = blob_list(c)?;
    let root = cas_root.join("objects");
    for b in &registered {
        report.checked_blobs += 1;
        if !valid_digest(&b.sha256) || b.size_bytes < 0 {
            report.push(
                "INVALID_BLOB",
                &b.sha256.chars().take(12).collect::<String>(),
            );
            continue;
        }
        let file = root.join(&b.sha256);
        match sha256_file(&file) {
            Ok((digest, size)) if digest == b.sha256 && size == b.size_bytes as u64 => {
                report.verified_blobs += 1
            }
            Ok((_, size)) if size != b.size_bytes as u64 => {
                report.push("BLOB_SIZE", &b.sha256[..12])
            }
            Ok(_) => report.push("BLOB_HASH", &b.sha256[..12]),
            Err(_) if !file.exists() => report.push("MISSING_BLOB", &b.sha256[..12]),
            Err(_) => report.push("UNSAFE_BLOB", &b.sha256[..12]),
        }
    }
    inspect_objects(
        &root,
        &cas_root.join("staging"),
        &registered,
        c,
        &mut report,
    )?;
    Ok(report)
}
fn valid_digest(s: &str) -> bool {
    s.len() == 64
        && s.bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
}
fn clean_dataset(c: &Connection, cas_root: &Path) -> Result<SafetyReport, TreeError> {
    let report = inspect(c, cas_root)?;
    if !report.clean() {
        return Err(failure("INTEGRITY_FAILED"));
    }
    Ok(report)
}
pub(super) fn execute_at(
    database: &Path,
    snapshots: &Path,
    restores: &Path,
    req: SafetyRequest,
    progress: &dyn Fn(SafetyProgress),
) -> Result<SafetyResponse, TreeError> {
    validate(&req)?;
    let action = req.action.clone();
    let mut response = SafetyResponse {
        action: action.clone(),
        status: "completed".into(),
        snapshot_id: req.snapshot_id.clone(),
        restore_label: req.restore_label.clone(),
        report: None,
        snapshots: Vec::new(),
    };
    match action.as_str() {
        "list" => {
            response.snapshots = snapshot::list_snapshots(snapshots)?;
        }
        "audit" => {
            let c = open_recognized(database)?;
            response.report = Some(inspect(
                &c,
                &database
                    .parent()
                    .ok_or_else(|| failure("UNSAFE_PATH"))?
                    .join("cas"),
            )?);
            response.status = if response.report.as_ref().is_some_and(SafetyReport::clean) {
                "clean"
            } else {
                "issues_found"
            }
            .into();
        }
        "backup" => {
            let manifest = snapshot::backup(database, snapshots, progress)?;
            response.snapshot_id = Some(manifest.snapshot_id);
            response.status = "verified".into();
        }
        "verify" => {
            let id = req
                .snapshot_id
                .as_deref()
                .ok_or_else(|| failure("INVALID_REQUEST"))?;
            let (manifest, report) = snapshot::verify_snapshot(snapshots, id, progress)?;
            response.snapshot_id = Some(manifest.snapshot_id);
            response.report = Some(report);
            response.status = "verified".into();
        }
        "restore" => {
            let id = req
                .snapshot_id
                .as_deref()
                .ok_or_else(|| failure("INVALID_REQUEST"))?;
            let label = req
                .restore_label
                .as_deref()
                .ok_or_else(|| failure("INVALID_REQUEST"))?;
            let report = snapshot::restore_snapshot(snapshots, restores, id, label, progress)?;
            response.report = Some(report);
            response.status = "restored".into();
        }
        _ => return Err(failure("INVALID_REQUEST")),
    }
    Ok(response)
}
#[tauri::command]
pub async fn repository_safety<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: SafetyRequest,
) -> Result<SafetyResponse, TreeError> {
    let database = database_path(&app)?;
    let app_root = app
        .path()
        .app_local_data_dir()
        .map_err(|_| failure("UNSAFE_PATH"))?;
    let snapshots = app_root.join("s7-snapshots");
    let restores = app_root.join("s7-restores");
    tauri::async_runtime::spawn_blocking(move || {
        let emitter = app.clone();
        execute_at(&database, &snapshots, &restores, request, &|state| {
            let _ = emitter.emit("data-m-s7-progress", state);
        })
    })
    .await
    .map_err(|_| failure("DISK_WRITE_FAILED"))?
}
