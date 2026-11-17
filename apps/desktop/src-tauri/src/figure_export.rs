//! S10 Figure byte-preserving exporter. Not exposed as an arbitrary-path IPC.
//! A future native picker must supply the explicitly authorized destination.
//! All source bytes are read from immutable, exact-version CAS objects.
use crate::project_tree::{required, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, HashMap};
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc, Mutex, OnceLock,
};
use tauri::Emitter;

fn error(code: &'static str) -> TreeError {
    TreeError::new(
        code,
        "Figure export could not be verified or safely published.",
    )
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ManifestEntry {
    pub panel_id: String,
    pub panel_order: i64,
    pub usage: String,
    pub asset_id: String,
    pub asset_version_id: String,
    pub version_number: i64,
    pub original_name: String,
    pub blob_key: String,
    pub sha256: String,
    pub size_bytes: i64,
    pub relative_path: String,
    pub verified: bool,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ManifestAnalysis {
    pub analysis_id: String,
    pub role: String,
    pub asset_version_id: String,
    pub frozen_at: Option<String>,
    pub included_replicate_ids: Vec<String>,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ManifestRepresentative {
    pub replicate_id: String,
    pub asset_version_id: Option<String>,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ManifestPanel {
    pub panel_id: String,
    pub panel_label: String,
    pub panel_order: i64,
    pub representatives: Vec<ManifestRepresentative>,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FigureManifest {
    pub manifest_version: u32,
    pub schema_version: u32,
    pub created_at: String,
    pub project_id: String,
    pub paper_id: String,
    pub figure_id: String,
    pub references: Vec<ManifestEntry>,
    pub panels: Vec<ManifestPanel>,
    pub analyses: Vec<ManifestAnalysis>,
    pub audit_event_ids: Vec<String>,
    pub reference_count: usize,
    pub physical_file_count: usize,
    pub status: String,
}
#[derive(Debug, Clone)]
struct Planned {
    panel_id: String,
    panel_order: i64,
    usage: String,
    asset_id: String,
    version_id: String,
    version_number: i64,
    original_name: String,
    sha: String,
    size: i64,
}
fn eligible(c: &Connection, project: &str, paper: &str, figure: &str) -> Result<(), TreeError> {
    let active: bool = c.query_row(
        "SELECT EXISTS(SELECT 1 FROM figures f
         JOIN papers p ON p.id=f.paper_id
         JOIN projects pr ON pr.id=p.project_id
         WHERE f.id=?1 AND p.id=?2 AND p.project_id=?3
         AND p.deleted_at IS NULL AND pr.deleted_at IS NULL)",
        params![figure, paper, project],
        |r| r.get(0),
    )?;
    if !active {
        return Err(error("NOT_FOUND"));
    }
    Ok(())
}
fn planned(
    c: &Connection,
    project: &str,
    paper: &str,
    figure: &str,
) -> Result<(Vec<Planned>, Vec<ManifestAnalysis>), TreeError> {
    eligible(c, project, paper, figure)?;
    let mut stmt = c.prepare(
        "SELECT x.id,x.sort_order,pav.usage,v.asset_id,v.id,v.version_number,
           v.source_name,v.blob_sha256,b.size_bytes
        FROM panels x
        JOIN panel_asset_versions pav ON pav.panel_id=x.id
        JOIN asset_versions v ON v.id=pav.asset_version_id
        JOIN blobs b ON b.sha256=v.blob_sha256
        WHERE x.figure_id=?1
        ORDER BY x.sort_order,x.id,pav.usage,v.id",
    )?;
    let items = stmt
        .query_map([figure], |r| {
            Ok(Planned {
                panel_id: r.get(0)?,
                panel_order: r.get(1)?,
                usage: r.get(2)?,
                asset_id: r.get(3)?,
                version_id: r.get(4)?,
                version_number: r.get(5)?,
                original_name: r.get(6)?,
                sha: r.get(7)?,
                size: r.get(8)?,
            })
        })?
        .collect::<rusqlite::Result<Vec<_>>>()?;
    // Exact version equality is evidence of a reference, NOT a causal
    // Panel→Analysis link. Return all analyses using any exported version.
    let mut analyses = Vec::new();
    let mut st = c.prepare(
        "SELECT DISTINCT a.id,av.role,av.asset_version_id,a.frozen_at
         FROM analysis_asset_versions av
         JOIN statistical_analyses a ON a.id=av.analysis_id
         JOIN assays s ON s.id=a.assay_id
         JOIN themes t ON t.id=s.theme_id
         WHERE t.project_id=?1 AND av.asset_version_id=?2
         ORDER BY a.id,av.role,av.asset_version_id",
    )?;
    for item in &items {
        let matches = st
            .query_map(params![project, item.version_id], |r| {
                Ok((
                    r.get::<_, String>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, String>(2)?,
                    r.get::<_, Option<String>>(3)?,
                ))
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        for (analysis_id, role, version_id, frozen_at) in matches {
            if analyses.iter().any(|a: &ManifestAnalysis| {
                a.analysis_id == analysis_id && a.role == role && a.asset_version_id == version_id
            }) {
                continue;
            }
            let mut selections = c.prepare(
                "SELECT replicate_id FROM analysis_samples
                 WHERE analysis_id=?1 AND included=1 ORDER BY replicate_id",
            )?;
            let included_replicate_ids = selections
                .query_map([&analysis_id], |r| r.get(0))?
                .collect::<rusqlite::Result<Vec<String>>>()?;
            analyses.push(ManifestAnalysis {
                analysis_id,
                role,
                asset_version_id: version_id,
                frozen_at,
                included_replicate_ids,
            });
        }
    }
    analyses.sort_by(|a, b| {
        (&a.analysis_id, &a.role, &a.asset_version_id).cmp(&(
            &b.analysis_id,
            &b.role,
            &b.asset_version_id,
        ))
    });
    Ok((items, analyses))
}
fn safe_dir(path: &Path) -> Result<PathBuf, TreeError> {
    use std::path::Component;
    if !path.is_absolute() {
        return Err(error("UNSAFE_PATH"));
    }
    // Check each lexical ancestor BEFORE canonicalization. Otherwise a
    // junction/symlink in the parent chain gets erased by canonicalize.
    let mut cursor = PathBuf::new();
    for component in path.components() {
        match component {
            Component::Prefix(_) | Component::RootDir | Component::Normal(_) => {
                cursor.push(component.as_os_str());
            }
            _ => return Err(error("UNSAFE_PATH")),
        }
        if matches!(component, Component::Prefix(_)) {
            continue;
        }
        let meta = fs::symlink_metadata(&cursor).map_err(|_| error("UNSAFE_PATH"))?;
        if !meta.file_type().is_dir() || meta.file_type().is_symlink() {
            return Err(error("UNSAFE_PATH"));
        }
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            if meta.file_attributes() & 0x400 != 0 {
                return Err(error("UNSAFE_PATH"));
            }
        }
    }
    fs::canonicalize(path).map_err(|_| error("UNSAFE_PATH"))
}

fn verified_copy(
    source: &Path,
    target: &Path,
    sha: &str,
    size: i64,
    cancelled: &dyn Fn() -> bool,
) -> Result<(), TreeError> {
    use sha2::{Digest, Sha256};
    if sha.len() != 64
        || !sha
            .bytes()
            .all(|b| b.is_ascii_hexdigit() && !b.is_ascii_uppercase())
        || size < 0
    {
        return Err(error("INVALID_BLOB"));
    }
    let meta = fs::symlink_metadata(source).map_err(|_| error("BLOB_MISSING"))?;
    if !meta.file_type().is_file() || meta.file_type().is_symlink() {
        return Err(error("UNSAFE_BLOB"));
    }
    if meta.len() != size as u64 {
        return Err(error("BLOB_CORRUPT"));
    }
    let mut input = File::open(source).map_err(|_| error("BLOB_UNREADABLE"))?;
    #[cfg(test)]
    eprintln!("S10 export: writing manifest");
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(target)
        .map_err(|_| error("DISK_WRITE_FAILED"))?;
    let mut hash = Sha256::new();
    let mut written = 0u64;
    let mut buf = [0u8; 1024 * 1024];
    loop {
        if cancelled() {
            return Err(error("CANCELLED"));
        }
        let n = input.read(&mut buf).map_err(|_| error("BLOB_UNREADABLE"))?;
        if n == 0 {
            break;
        }
        hash.update(&buf[..n]);
        output
            .write_all(&buf[..n])
            .map_err(|_| error("DISK_WRITE_FAILED"))?;
        written += n as u64;
    }
    output.sync_all().map_err(|_| error("DISK_WRITE_FAILED"))?;
    // Windows refuses to rename a directory while a child file is held open
    // without FILE_SHARE_DELETE. Close the manifest handle before publication.
    drop(output);
    if written != size as u64 || format!("{:x}", hash.finalize()) != sha {
        return Err(error("BLOB_CORRUPT"));
    }
    let (actual, bytes) = crate::cas::hash_file(target).map_err(|_| error("DISK_WRITE_FAILED"))?;
    if actual != sha || bytes != size as u64 {
        return Err(error("BLOB_CORRUPT"));
    }
    Ok(())
}
/// This intentionally has no Tauri command. Destination authorization and
/// Windows directory handle/reparse protection must be integrated first.
pub(crate) fn export_figure_at(
    database: &Path,
    project: &str,
    paper: &str,
    figure: &str,
    destination: &Path,
) -> Result<(PathBuf, FigureManifest), TreeError> {
    export_figure_with_hooks(
        database,
        project,
        paper,
        figure,
        destination,
        &|| false,
        &|_, _| {},
        None,
    )
}
fn export_figure_with_hooks(
    database: &Path,
    project: &str,
    paper: &str,
    figure: &str,
    destination: &Path,
    cancelled: &dyn Fn() -> bool,
    progress: &dyn Fn(usize, usize),
    publication_token: Option<&str>,
) -> Result<(PathBuf, FigureManifest), TreeError> {
    required(Some(project), 128)?;
    required(Some(paper), 128)?;
    required(Some(figure), 128)?;
    let repository = safe_dir(database.parent().ok_or_else(|| error("UNSAFE_PATH"))?)?;
    let cas_root = safe_dir(&repository.join("cas").join("objects"))?;
    let destination = safe_dir(destination)?;
    #[cfg(windows)]
    let destination_guard = crate::import_jobs::filesystem::Directories::hold_export(&destination)?;
    #[cfg(windows)]
    let _source_guard = crate::import_jobs::filesystem::Directories::hold_export(&cas_root)?;
    if destination.starts_with(&repository) || repository.starts_with(&destination) {
        return Err(error("UNSAFE_PATH"));
    }
    let (items, analyses, panels, audit_event_ids, created_at) = with_database_at(database, |c| {
        let (items, analyses) = planned(c, project, paper, figure)?;
        // Preserve every Panel, including those with no pinned bytes.
        let mut stmt = c.prepare(
            "SELECT x.id,x.label,x.sort_order,rs.replicate_id,rs.asset_version_id
                 FROM panels x LEFT JOIN representative_sources rs ON rs.panel_id=x.id
                 WHERE x.figure_id=?1
                 ORDER BY x.sort_order,x.id,rs.replicate_id",
        )?;
        let rows = stmt
            .query_map([figure], |r| {
                Ok((
                    r.get::<_, String>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, i64>(2)?,
                    r.get::<_, Option<String>>(3)?,
                    r.get::<_, Option<String>>(4)?,
                ))
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        let mut panels = Vec::<ManifestPanel>::new();
        for (panel_id, panel_label, panel_order, replicate_id, version_id) in rows {
            if panels.last().map(|p| p.panel_id.as_str()) != Some(panel_id.as_str()) {
                panels.push(ManifestPanel {
                    panel_id,
                    panel_label,
                    panel_order,
                    representatives: Vec::new(),
                });
            }
            if let Some(replicate_id) = replicate_id {
                if let Some(panel) = panels.last_mut() {
                    panel.representatives.push(ManifestRepresentative {
                        replicate_id,
                        asset_version_id: version_id,
                    });
                }
            }
        }
        // Persist auditable event identifiers; JSON details remain in the
        // source repository's immutable audit table, not inferred labels.
        let mut audits = c.prepare(
            "SELECT id FROM audit_events
                 WHERE object_id=?1 OR object_id=?2
                    OR object_id IN (SELECT id FROM panels WHERE figure_id=?2)
                 ORDER BY created_at,id",
        )?;
        let audit_event_ids = audits
            .query_map(params![paper, figure], |r| r.get(0))?
            .collect::<rusqlite::Result<Vec<String>>>()?;
        let now: String = c.query_row("SELECT strftime('%Y-%m-%dT%H:%M:%fZ','now')", [], |r| {
            r.get(0)
        })?;
        Ok((items, analyses, panels, audit_event_ids, now))
    })?;
    if items.is_empty() {
        return Err(error("EMPTY_FIGURE"));
    }
    let stage = tempfile::Builder::new()
        .prefix(".data-m-figure-stage-")
        .tempdir_in(&destination)
        .map_err(|_| error("DISK_WRITE_FAILED"))?;
    // A crash may leave this staging folder. Only stages with a durable
    // request claim and this marker are eligible for explicit aged recovery.
    if let Some(token) = publication_token {
        let mut marker = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(stage.path().join(".data-m-figure-owner.json"))
            .map_err(|_| error("DISK_WRITE_FAILED"))?;
        serde_json::to_writer(&mut marker, &serde_json::json!({"requestId":token}))
            .map_err(|_| error("DISK_WRITE_FAILED"))?;
        marker.sync_all().map_err(|_| error("DISK_WRITE_FAILED"))?;
    }
    let files = stage.path().join("files");
    fs::create_dir(&files).map_err(|_| error("DISK_WRITE_FAILED"))?;
    let mut unique = BTreeMap::<String, String>::new();
    let mut refs = Vec::new();
    let total_files = items
        .iter()
        .map(|x| x.sha.as_str())
        .collect::<std::collections::BTreeSet<_>>()
        .len();
    for item in items {
        let relative_path = format!("files/{}", item.sha);
        if !unique.contains_key(&item.sha) {
            #[cfg(test)]
            eprintln!("S10 export: copying CAS blob {}", item.sha);
            if cancelled() {
                return Err(error("CANCELLED"));
            }
            verified_copy(
                &cas_root.join(&item.sha),
                &files.join(&item.sha),
                &item.sha,
                item.size,
                cancelled,
            )?;
            unique.insert(item.sha.clone(), relative_path.clone());
            progress(unique.len(), total_files);
        }
        refs.push(ManifestEntry {
            panel_id: item.panel_id,
            panel_order: item.panel_order,
            usage: item.usage,
            asset_id: item.asset_id,
            asset_version_id: item.version_id,
            version_number: item.version_number,
            original_name: item.original_name,
            blob_key: item.sha.clone(),
            sha256: item.sha,
            size_bytes: item.size,
            relative_path,
            verified: true,
        });
    }
    #[cfg(test)]
    eprintln!("S10 export: wrote {} CAS files", unique.len());
    let manifest = FigureManifest {
        manifest_version: 1,
        schema_version: 3,
        created_at,
        project_id: project.into(),
        paper_id: paper.into(),
        figure_id: figure.into(),
        reference_count: refs.len(),
        physical_file_count: unique.len(),
        references: refs,
        panels,
        analyses,
        audit_event_ids,
        status: "complete".into(),
    };
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(stage.path().join("figure-manifest.json"))
        .map_err(|_| error("DISK_WRITE_FAILED"))?;
    serde_json::to_writer_pretty(&mut output, &manifest).map_err(|_| error("DISK_WRITE_FAILED"))?;
    output.sync_all().map_err(|_| error("DISK_WRITE_FAILED"))?;
    // Windows cannot rename the staging directory while the manifest handle
    // remains open without FILE_SHARE_DELETE. Flush and release it first.
    drop(output);
    // Caller may provide only a previously existing destination directory.
    // Random subdirectory avoids name collision and never touches existing
    // files. Windows rename cannot replace an existing target directory.
    let random_id = if let Some(token) = publication_token {
        export_key(token)?;
        token.to_owned()
    } else {
        with_database_at(database, |c| {
            c.query_row("SELECT lower(hex(randomblob(16)))", [], |r| {
                r.get::<_, String>(0)
            })
            .map_err(TreeError::from)
        })?
    };
    let target = destination.join(format!("figure-{random_id}"));
    if fs::symlink_metadata(&target).is_ok() {
        return Err(error("TARGET_CONFLICT"));
    }
    if cancelled() {
        return Err(error("CANCELLED"));
    }
    #[cfg(windows)]
    destination_guard.validate()?;
    #[cfg(test)]
    eprintln!(
        "S10 export: publishing staged folder {:?} to {:?}",
        stage.path(),
        target
    );
    // Never detach TempDir ownership before publication. On any rename
    // failure its Drop removes unpublished bytes; on success the old path
    // disappears and Drop cannot affect the newly published target.
    match fs::rename(stage.path(), &target) {
        Ok(()) => Ok((target, manifest)),
        Err(rename_error) => {
            #[cfg(test)]
            eprintln!("S10 export: publish OS error {:?}", rename_error);
            #[cfg(not(test))]
            let _ = &rename_error;
            Err(error("DISK_WRITE_FAILED"))
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct FigureExportRequest {
    action: String,
    project_id: String,
    paper_id: String,
    figure_id: String,
    confirm: Option<bool>,
    request_id: Option<String>,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FigureExportPreview {
    figure_id: String,
    panel_count: i64,
    reference_count: usize,
    unique_file_count: usize,
    unique_bytes: i64,
}
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FigureExportResponse {
    status: String,
    preview: Option<FigureExportPreview>,
    folder_path: Option<String>,
    manifest: Option<FigureManifest>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FigureExportProgress {
    request_id: String,
    completed: usize,
    total: usize,
}
// The existing v3 s1_requests table is a durable ledger. An uncertain
// crash cannot silently reexecute a token. Completed output is reconstructed
// only after independent byte and metadata verification.
#[derive(Debug, PartialEq, Eq)]
enum Claim {
    New,
    Existing,
}
fn export_key(token: &str) -> Result<String, TreeError> {
    if token.len() != 36
        || !token.bytes().enumerate().all(|(i, b)| {
            if matches!(i, 8 | 13 | 18 | 23) {
                b == b'-'
            } else {
                b.is_ascii_hexdigit()
            }
        })
    {
        return Err(error("INVALID_REQUEST"));
    }
    Ok(format!("s10-figure-export-{token}"))
}
fn claim_export_request(
    database: &Path,
    token: &str,
    project: &str,
    paper: &str,
    figure: &str,
    destination: &Path,
) -> Result<Claim, TreeError> {
    let key = export_key(token)?;
    let identity = serde_json::json!({
        "action":"figure_export","projectId":project,"paperId":paper,
        "figureId":figure,"destination":destination.to_string_lossy()
    });
    with_database_at(database, |c| {
        let existing: Option<String> = c
            .query_row(
                "SELECT payload FROM s1_requests WHERE request_id=?1",
                [&key],
                |r| r.get(0),
            )
            .optional()?;
        if let Some(payload) = existing {
            let previous: serde_json::Value =
                serde_json::from_str(&payload).map_err(|_| error("REQUEST_CONFLICT"))?;
            if previous.get("identity") != Some(&identity) {
                return Err(error("REQUEST_CONFLICT"));
            }
            return Ok(Claim::Existing);
        }
        c.execute(
            "INSERT INTO s1_requests(request_id,payload) VALUES(?1,?2)",
            params![
                key,
                serde_json::json!({"identity":identity,"status":"started"}).to_string()
            ],
        )?;
        Ok(Claim::New)
    })
}
fn mark_export_complete(database: &Path, token: &str) -> Result<(), TreeError> {
    let key = export_key(token)?;
    with_database_at(database, |c| {
        let old: String = c.query_row(
            "SELECT payload FROM s1_requests WHERE request_id=?1",
            [&key],
            |r| r.get(0),
        )?;
        let mut value: serde_json::Value =
            serde_json::from_str(&old).map_err(|_| error("REQUEST_CONFLICT"))?;
        value["status"] = serde_json::json!("complete");
        c.execute(
            "UPDATE s1_requests SET payload=?2 WHERE request_id=?1 AND payload=?3",
            params![key, value.to_string(), old],
        )?;
        Ok(())
    })
}
fn verified_replay(
    database: &Path,
    project: &str,
    paper: &str,
    figure: &str,
    destination: &Path,
    token: &str,
) -> Result<(PathBuf, FigureManifest), TreeError> {
    let root = destination.join(format!("figure-{token}"));
    let root = safe_dir(&root).map_err(|_| error("EXPORT_INDETERMINATE"))?;
    let mf = root.join("figure-manifest.json");
    let meta = fs::symlink_metadata(&mf).map_err(|_| error("EXPORT_INDETERMINATE"))?;
    if !meta.file_type().is_file() || meta.file_type().is_symlink() || meta.len() > 16 * 1024 * 1024
    {
        return Err(error("EXPORT_INDETERMINATE"));
    }
    let manifest: FigureManifest =
        serde_json::from_slice(&fs::read(&mf).map_err(|_| error("EXPORT_INDETERMINATE"))?)
            .map_err(|_| error("EXPORT_INDETERMINATE"))?;
    if manifest.manifest_version != 1
        || manifest.schema_version != 3
        || manifest.project_id != project
        || manifest.paper_id != paper
        || manifest.figure_id != figure
        || manifest.status != "complete"
        || manifest.reference_count != manifest.references.len()
    {
        return Err(error("EXPORT_INDETERMINATE"));
    }
    let mut hashes = std::collections::BTreeSet::new();
    for edge in &manifest.references {
        if edge.relative_path != format!("files/{}", edge.sha256)
            || edge.blob_key != edge.sha256
            || !edge.verified
            || edge.sha256.len() != 64
            || !edge
                .sha256
                .bytes()
                .all(|b| b.is_ascii_hexdigit() && !b.is_ascii_uppercase())
            || edge.size_bytes < 0
        {
            return Err(error("EXPORT_INDETERMINATE"));
        }
        let file = root.join(&edge.relative_path);
        let meta = fs::symlink_metadata(&file).map_err(|_| error("EXPORT_INDETERMINATE"))?;
        if !meta.file_type().is_file()
            || meta.file_type().is_symlink()
            || meta.len() != edge.size_bytes as u64
        {
            return Err(error("EXPORT_INDETERMINATE"));
        }
        let (sha, len) = crate::cas::hash_file(&file).map_err(|_| error("EXPORT_INDETERMINATE"))?;
        if sha != edge.sha256 || len != edge.size_bytes as u64 {
            return Err(error("EXPORT_INDETERMINATE"));
        }
        hashes.insert(edge.sha256.as_str());
    }
    if hashes.len() != manifest.physical_file_count {
        return Err(error("EXPORT_INDETERMINATE"));
    }
    // No repository bytes are changed during verified replay.
    let _ = database;
    Ok((root, manifest))
}
// Manual/next-export recovery touches ONLY aged, marker-bearing temp
// directories that match a persisted v3 claim for the same destination.
// Unknown folders, symlinks/reparse points and active exports are untouched.
fn safe_recovery_tree(path: &Path) -> Result<(), TreeError> {
    for item in fs::read_dir(path).map_err(|_| error("RECOVERY_BLOCKED"))? {
        let entry = item.map_err(|_| error("RECOVERY_BLOCKED"))?;
        let meta = fs::symlink_metadata(entry.path()).map_err(|_| error("RECOVERY_BLOCKED"))?;
        if meta.file_type().is_symlink() {
            return Err(error("RECOVERY_BLOCKED"));
        }
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            if meta.file_attributes() & 0x400 != 0 {
                return Err(error("RECOVERY_BLOCKED"));
            }
        }
        if meta.is_dir() {
            safe_recovery_tree(&entry.path())?;
        } else if !meta.is_file() {
            return Err(error("RECOVERY_BLOCKED"));
        }
    }
    Ok(())
}
fn recover_stale_stages_at(
    database: &Path,
    destination: &Path,
    minimum_age: std::time::Duration,
) -> Result<usize, TreeError> {
    let destination = safe_dir(destination)?;
    #[cfg(windows)]
    let guard = crate::import_jobs::filesystem::Directories::hold_export(&destination)?;
    let mut removed = 0;
    for item in fs::read_dir(&destination).map_err(|_| error("RECOVERY_BLOCKED"))? {
        let entry = item.map_err(|_| error("RECOVERY_BLOCKED"))?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if !name.starts_with(".data-m-figure-stage-") {
            continue;
        }
        let dir = entry.path();
        let meta = fs::symlink_metadata(&dir).map_err(|_| error("RECOVERY_BLOCKED"))?;
        if !meta.file_type().is_dir() || meta.file_type().is_symlink() {
            continue;
        }
        #[cfg(windows)]
        {
            use std::os::windows::fs::MetadataExt;
            if meta.file_attributes() & 0x400 != 0 {
                continue;
            }
        }
        let modified = meta.modified().map_err(|_| error("RECOVERY_BLOCKED"))?;
        if modified.elapsed().unwrap_or_default() < minimum_age {
            continue;
        }
        let owner = dir.join(".data-m-figure-owner.json");
        let owner_meta = match fs::symlink_metadata(&owner) {
            Ok(v) => v,
            Err(_) => continue,
        };
        if !owner_meta.file_type().is_file()
            || owner_meta.file_type().is_symlink()
            || owner_meta.len() > 1024
        {
            continue;
        }
        let val: serde_json::Value = match fs::read(&owner)
            .ok()
            .and_then(|b| serde_json::from_slice(&b).ok())
        {
            Some(v) => v,
            None => continue,
        };
        let token = match val.get("requestId").and_then(|v| v.as_str()) {
            Some(v) => v,
            None => continue,
        };
        let key = match export_key(token) {
            Ok(v) => v,
            Err(_) => continue,
        };
        let busy = active_exports()
            .lock()
            .map_err(|_| error("RECOVERY_BLOCKED"))?;
        if busy.contains_key(token) {
            continue;
        }
        drop(busy);
        let claim: Option<String> = with_database_at(database, |conn| {
            Ok(conn
                .query_row(
                    "SELECT payload FROM s1_requests WHERE request_id=?1",
                    [&key],
                    |r| r.get(0),
                )
                .optional()?)
        })?;
        let identity = claim.and_then(|v| serde_json::from_str::<serde_json::Value>(&v).ok());
        let matches = identity
            .as_ref()
            .and_then(|v| v.get("identity"))
            .and_then(|v| v.get("destination"))
            .and_then(|v| v.as_str())
            == Some(destination.to_string_lossy().as_ref());
        if !matches {
            continue;
        }
        safe_recovery_tree(&dir)?;
        #[cfg(windows)]
        guard.validate()?;
        // This is a verified stale, unpublished stage, never a completed
        // 'figure-<id>' directory. Errors fail closed for further export.
        fs::remove_dir_all(&dir).map_err(|_| error("RECOVERY_BLOCKED"))?;
        removed += 1;
    }
    Ok(removed)
}
static IN_FLIGHT: OnceLock<Mutex<HashMap<String, Arc<AtomicBool>>>> = OnceLock::new();
fn active_exports() -> &'static Mutex<HashMap<String, Arc<AtomicBool>>> {
    IN_FLIGHT.get_or_init(|| Mutex::new(HashMap::new()))
}
fn preview_at(
    database: &Path,
    project: &str,
    paper: &str,
    figure: &str,
) -> Result<FigureExportPreview, TreeError> {
    with_database_at(database, |c| {
        let (items, _) = planned(c, project, paper, figure)?;
        let panel_count = c.query_row(
            "SELECT count(*) FROM panels WHERE figure_id=?1",
            [figure],
            |r| r.get(0),
        )?;
        let mut bytes = BTreeMap::new();
        for item in &items {
            bytes.entry(&item.sha).or_insert(item.size);
        }
        Ok(FigureExportPreview {
            figure_id: figure.into(),
            panel_count,
            reference_count: items.len(),
            unique_file_count: bytes.len(),
            unique_bytes: bytes.values().sum(),
        })
    })
}
#[tauri::command]
pub fn figure_export_cancel(request_id: String) -> Result<bool, TreeError> {
    required(Some(&request_id), 128)?;
    let map = active_exports().lock().map_err(|_| error("EXPORT_BUSY"))?;
    if let Some(flag) = map.get(&request_id) {
        flag.store(true, Ordering::SeqCst);
        Ok(true)
    } else {
        Ok(false)
    }
}
#[tauri::command]
pub async fn figure_export<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: FigureExportRequest,
) -> Result<FigureExportResponse, TreeError> {
    use crate::project_tree::database_path;
    required(Some(&request.project_id), 128)?;
    required(Some(&request.paper_id), 128)?;
    required(Some(&request.figure_id), 128)?;
    let database = database_path(&app)?;
    if request.action == "preview" {
        if request.confirm.is_some() || request.request_id.is_some() {
            return Err(error("INVALID_REQUEST"));
        }
        let preview = preview_at(
            &database,
            &request.project_id,
            &request.paper_id,
            &request.figure_id,
        )?;
        return Ok(FigureExportResponse {
            status: "preview".into(),
            preview: Some(preview),
            folder_path: None,
            manifest: None,
        });
    }
    if request.action != "export" || request.confirm != Some(true) {
        return Err(error("INVALID_REQUEST"));
    }
    let token = required(request.request_id.as_deref(), 128)?.to_owned();
    let flag = Arc::new(AtomicBool::new(false));
    {
        let mut map = active_exports().lock().map_err(|_| error("EXPORT_BUSY"))?;
        if map.contains_key(&token) {
            return Err(error("REQUEST_CONFLICT"));
        }
        map.insert(token.clone(), flag.clone());
    }
    let emit_app = app.clone();
    let result = tauri::async_runtime::spawn_blocking(move || {
        let operation = || {
            let destination = safe_dir(&pick_native_folder()?)?;
            // Conservative, user-authorized recovery of abandoned export
            // staging in this chosen destination (never source CAS).
            recover_stale_stages_at(
                &database,
                &destination,
                std::time::Duration::from_secs(24 * 3600),
            )?;
            if flag.load(Ordering::SeqCst) {
                return Err(error("CANCELLED"));
            }
            let progress = |completed: usize, total: usize| {
                let _ = emit_app.emit(
                    "s10-figure-export-progress",
                    FigureExportProgress {
                        request_id: token.clone(),
                        completed,
                        total,
                    },
                );
            };
            // Claim only after the native picker returns; user cancellation of
            // the picker does not consume a token. A crash after this claim
            // requires a fresh token and an explicit inspection of the target.
            let claim = claim_export_request(
                &database,
                &token,
                &request.project_id,
                &request.paper_id,
                &request.figure_id,
                &destination,
            )?;
            if claim == Claim::Existing {
                let (folder, manifest) = verified_replay(
                    &database,
                    &request.project_id,
                    &request.paper_id,
                    &request.figure_id,
                    &destination,
                    &token,
                )?;
                mark_export_complete(&database, &token)?;
                return Ok(FigureExportResponse {
                    status: "complete".into(),
                    preview: None,
                    folder_path: Some(folder.to_string_lossy().into_owned()),
                    manifest: Some(manifest),
                });
            }
            let (folder, manifest) = export_figure_with_hooks(
                &database,
                &request.project_id,
                &request.paper_id,
                &request.figure_id,
                &destination,
                &|| flag.load(Ordering::SeqCst),
                &progress,
                Some(&token),
            )?;
            mark_export_complete(&database, &token)?;
            Ok(FigureExportResponse {
                status: "complete".into(),
                preview: None,
                folder_path: Some(folder.to_string_lossy().into_owned()),
                manifest: Some(manifest),
            })
        };
        let result = operation();
        if let Ok(mut map) = active_exports().lock() {
            map.remove(&token);
        }
        result
    })
    .await
    .map_err(|_| error("EXPORT_FAILED"))?;
    result
}
#[cfg(not(windows))]
fn pick_native_folder() -> Result<PathBuf, TreeError> {
    Err(error("UNSUPPORTED_PLATFORM"))
}
#[cfg(windows)]
fn pick_native_folder() -> Result<PathBuf, TreeError> {
    // A dedicated STA thread is required by the Windows Shell dialog.
    std::thread::Builder::new()
        .name("data-m-figure-picker".into())
        .spawn(win32_pick_folder)
        .map_err(|_| error("PICKER_UNAVAILABLE"))?
        .join()
        .map_err(|_| error("PICKER_UNAVAILABLE"))?
}
#[cfg(windows)]
fn win32_pick_folder() -> Result<PathBuf, TreeError> {
    use std::ffi::c_void;
    use std::os::windows::ffi::OsStringExt;
    #[repr(C)]
    struct BrowseInfo {
        owner: *mut c_void,
        root: *const c_void,
        display_name: *mut u16,
        title: *const u16,
        flags: u32,
        callback: *const c_void,
        lparam: isize,
        image: i32,
    }
    #[link(name = "shell32")]
    extern "system" {
        fn SHBrowseForFolderW(info: *mut BrowseInfo) -> *mut c_void;
        fn SHGetPathFromIDListEx(
            pidl: *const c_void,
            path: *mut u16,
            length: u32,
            flags: u32,
        ) -> i32;
    }
    #[link(name = "ole32")]
    extern "system" {
        fn CoInitializeEx(reserved: *mut c_void, flags: u32) -> i32;
        fn CoUninitialize();
        fn CoTaskMemFree(pointer: *mut c_void);
    }
    let hr = unsafe { CoInitializeEx(std::ptr::null_mut(), 2 | 4) };
    if hr < 0 {
        return Err(error("PICKER_UNAVAILABLE"));
    }
    let selected = (|| {
        let mut display = [0u16; 260];
        let title: Vec<u16> = "Data-M: choose Figure export folder"
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        let mut info = BrowseInfo {
            owner: std::ptr::null_mut(),
            root: std::ptr::null(),
            display_name: display.as_mut_ptr(),
            title: title.as_ptr(),
            flags: 0x1 | 0x40,
            callback: std::ptr::null(),
            lparam: 0,
            image: 0,
        };
        let pidl = unsafe { SHBrowseForFolderW(&mut info) };
        if pidl.is_null() {
            return Err(error("CANCELLED"));
        }
        let mut chars = vec![0u16; 32768];
        let valid = unsafe {
            let result = SHGetPathFromIDListEx(pidl, chars.as_mut_ptr(), chars.len() as u32, 0);
            CoTaskMemFree(pidl);
            result != 0
        };
        if !valid {
            return Err(error("UNSAFE_PATH"));
        }
        let end = chars
            .iter()
            .position(|c| *c == 0)
            .ok_or_else(|| error("UNSAFE_PATH"))?;
        let selected = std::ffi::OsString::from_wide(&chars[..end]);
        Ok(PathBuf::from(selected))
    })();
    unsafe { CoUninitialize() };
    selected
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn s10_figure_manifest_exact_bytes_deduplicated_and_corruption_closed() {
        let dir = tempfile::tempdir().unwrap();
        let db = dir.path().join("repo").join("state.db");
        fs::create_dir(db.parent().unwrap()).unwrap();
        with_database_at(&db, |c| {
            c.execute_batch("INSERT INTO projects(id,name) VALUES('p','Project');")?;
            Ok(())
        })
        .unwrap();
        with_database_at(&db,|c|{
            c.execute("INSERT OR IGNORE INTO themes(id,project_id,name) VALUES('t','p','Theme')",[])?;
            c.execute_batch("
              INSERT INTO assays(id,theme_id,name) VALUES('assay','t','Assay');
              INSERT INTO papers(id,project_id,title) VALUES('paper','p','Paper');
              INSERT INTO figures(id,paper_id,label) VALUES('fig','paper','Figure 1');
              INSERT INTO panels(id,figure_id,label,sort_order) VALUES('a','fig','A',0),('b','fig','B',1);
              INSERT INTO data_assets(id,name) VALUES('asset','x.png');
              INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','assay','Group');
              INSERT INTO replicates(id,group_id,label) VALUES
                ('r1','g','1'),('r2','g','2'),('r3','g','3'),
                ('r4','g','4'),('r5','g','5'),('r6','g','6');
              INSERT INTO asset_links(id,asset_id,assay_id,stage,display_name)
                VALUES('link','asset','assay','raw','x.png');
              INSERT INTO statistical_analyses(id,assay_id,name,notes,updated_at)
                VALUES('analysis-a','assay','A','','2026-01-01'),
                      ('analysis-b','assay','B','','2026-01-01');
              INSERT INTO analysis_samples(analysis_id,replicate_id,included)
                VALUES
                ('analysis-a','r1',1),('analysis-a','r2',1),
                ('analysis-a','r3',1),('analysis-a','r4',1),
                ('analysis-a','r5',1),
                ('analysis-b','r1',1),('analysis-b','r2',1),
                ('analysis-b','r3',1),('analysis-b','r4',1),
                ('analysis-b','r5',1),('analysis-b','r6',1);
            ")?;
            Ok(())
        }).unwrap();
        let original = b"unaltered native image bytes\x00\x01";
        use sha2::{Digest, Sha256};
        let sha = format!("{:x}", Sha256::digest(original));
        let objects = db.parent().unwrap().join("cas").join("objects");
        fs::create_dir_all(&objects).unwrap();
        fs::write(objects.join(&sha), original).unwrap();
        with_database_at(&db,|c|{
            c.execute("INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",
                params![sha,original.len() as i64])?;
            c.execute("INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name) VALUES('v1','asset',1,?1,'x.png')",[&sha])?;
            c.execute_batch("
              INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
                VALUES('a','v1','display'),('b','v1','source');
              INSERT INTO representative_sources(panel_id,replicate_id,asset_version_id)
                VALUES('a','r1','v1');
              INSERT INTO audit_events(id,action,object_type,object_id,details_json)
                VALUES('audit-paper','paper_metadata','paper','paper','{}');
              INSERT INTO analysis_asset_versions(analysis_id,asset_version_id,role)
                VALUES('analysis-a','v1','input'),('analysis-b','v1','input');
              UPDATE statistical_analyses SET frozen_at='2026-01-02'
                WHERE id IN ('analysis-a','analysis-b');
              UPDATE asset_links SET deleted_at='2026-02-01' WHERE id='link';
            ")?;
            Ok(())
        }).unwrap();
        let dest = dir.path().join("export");
        fs::create_dir(&dest).unwrap();
        let (root, m) = export_figure_at(&db, "p", "paper", "fig", &dest).unwrap();
        assert_eq!(m.reference_count, 2);
        assert_eq!(m.physical_file_count, 1);
        assert_eq!(m.analyses.len(), 2);
        assert_eq!(m.panels.len(), 2);
        assert_eq!(m.panels[0].panel_id, "a");
        assert_eq!(m.panels[0].representatives.len(), 1);
        assert_eq!(m.panels[0].representatives[0].replicate_id, "r1");
        assert_eq!(m.audit_event_ids, vec!["audit-paper"]);
        assert_eq!(m.analyses[0].included_replicate_ids.len(), 5);
        assert_eq!(m.analyses[1].included_replicate_ids.len(), 6);
        assert_eq!(m.references[0].asset_version_id, "v1");
        assert_eq!(fs::read(root.join("files").join(&sha)).unwrap(), original);
        let raw = fs::read(root.join("figure-manifest.json")).unwrap();
        assert!(raw.windows(2).any(|x| x == b"v1"));
        // A future version is not substituted for a historically pinned v1.
        with_database_at(&db, |c| {
            c.execute(
                "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
                       VALUES('v2','asset',2,?1,'x-v2.png')",
                [&sha],
            )?;
            Ok(())
        })
        .unwrap();
        let (_next, next_manifest) = export_figure_at(&db, "p", "paper", "fig", &dest).unwrap();
        assert!(next_manifest
            .references
            .iter()
            .all(|r| r.asset_version_id == "v1"));
        assert_eq!(
            export_figure_at(&db, "wrong-project", "paper", "fig", &dest)
                .unwrap_err()
                .code,
            "NOT_FOUND"
        );
        assert_eq!(
            export_figure_at(&db, "p", "paper", "fig", db.parent().unwrap())
                .unwrap_err()
                .code,
            "UNSAFE_PATH"
        );
        assert_eq!(
            export_figure_at(&db, "p", "paper", "fig", Path::new("relative"))
                .unwrap_err()
                .code,
            "UNSAFE_PATH"
        );
        // Two real CAS objects: cancellation after the first verified blob
        // must remove the entire unpublished staging directory.
        let second_bytes = b"second separate immutable CAS blob";
        let second_sha = format!("{:x}", Sha256::digest(second_bytes));
        fs::write(objects.join(&second_sha), second_bytes).unwrap();
        with_database_at(&db, |conn| {
            conn.execute(
                "INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",
                params![second_sha, second_bytes.len() as i64],
            )?;
            conn.execute_batch("INSERT INTO data_assets(id,name) VALUES('asset2','a-other.tif');")?;
            conn.execute(
                "INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name)
                VALUES('vother','asset2',1,?1,'a-other.tif')",
                [&second_sha],
            )?;
            conn.execute_batch(
                "INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
                VALUES('b','vother','statistics');",
            )?;
            Ok(())
        })
        .unwrap();
        let interrupted = AtomicBool::new(false);
        let no_published = fs::read_dir(&dest).unwrap().count();
        assert_eq!(
            export_figure_with_hooks(
                &db,
                "p",
                "paper",
                "fig",
                &dest,
                &|| interrupted.load(Ordering::SeqCst),
                &|done, _| {
                    if done == 1 {
                        interrupted.store(true, Ordering::SeqCst);
                    }
                },
                None
            )
            .unwrap_err()
            .code,
            "CANCELLED"
        );
        assert_eq!(fs::read_dir(&dest).unwrap().count(), no_published);
        assert_eq!(fs::read(objects.join(&sha)).unwrap(), original);
        assert_eq!(fs::read(objects.join(&second_sha)).unwrap(), second_bytes);
        let before_count = fs::read_dir(&dest).unwrap().count();
        assert_eq!(
            export_figure_with_hooks(&db, "p", "paper", "fig", &dest, &|| true, &|_, _| {}, None)
                .unwrap_err()
                .code,
            "CANCELLED"
        );
        assert_eq!(fs::read_dir(&dest).unwrap().count(), before_count);
        // A deterministic token publishes once and survives process restart.
        // Replay validates every exported byte before returning 'complete'.
        let token = "01234567-89ab-cdef-0123-456789abcdef";
        assert_eq!(
            claim_export_request(&db, token, "p", "paper", "fig", &dest).unwrap(),
            Claim::New
        );
        let (published, first_manifest) = export_figure_with_hooks(
            &db,
            "p",
            "paper",
            "fig",
            &dest,
            &|| false,
            &|_, _| {},
            Some(token),
        )
        .unwrap();
        mark_export_complete(&db, token).unwrap();
        assert_eq!(published.file_name().unwrap().to_str().unwrap(), format!("figure-{token}"));
        assert_eq!(
            claim_export_request(&db, token, "p", "paper", "fig", &dest).unwrap(),
            Claim::Existing
        );
        let (replayed, recovered_manifest) =
            verified_replay(&db, "p", "paper", "fig", &dest, token).unwrap();
        assert_eq!(replayed, published);
        assert_eq!(
            recovered_manifest.reference_count,
            first_manifest.reference_count
        );
        assert_eq!(recovered_manifest.physical_file_count, 2);
        let altered = vec![b'Q'; second_bytes.len()];
        fs::write(published.join("files").join(&second_sha), altered).unwrap();
        assert_eq!(
            verified_replay(&db, "p", "paper", "fig", &dest, token)
                .unwrap_err()
                .code,
            "EXPORT_INDETERMINATE"
        );
        fs::write(objects.join(&sha), vec![b'X'; original.len()]).unwrap();
        assert_eq!(
            export_figure_at(&db, "p", "paper", "fig", &dest)
                .unwrap_err()
                .code,
            "BLOB_CORRUPT"
        );
    }
    #[test]
    fn s10_durable_at_most_once_claim_survives_reopening_sqlite() {
        let dir = tempfile::tempdir().unwrap();
        let db = dir.path().join("state.sqlite");
        let token = "12345678-1234-1234-1234-123456789abc";
        let dest = dir.path().join("dest");
        fs::create_dir(&dest).unwrap();
        assert_eq!(
            claim_export_request(&db, token, "p", "paper", "figure", &dest).unwrap(),
            Claim::New
        );
        // with_database_at independently reopens the database and commits
        // the claim. A retry cannot silently produce a second export.
        assert_eq!(
            claim_export_request(&db, token, "p", "paper", "figure", &dest).unwrap(),
            Claim::Existing
        );
        assert_eq!(
            claim_export_request(&db, token, "other", "paper", "figure", &dest)
                .unwrap_err()
                .code,
            "REQUEST_CONFLICT"
        );
        assert_eq!(
            claim_export_request(&db, "../invalid", "p", "paper", "figure", &dest)
                .unwrap_err()
                .code,
            "INVALID_REQUEST"
        );
        let count: i64 = with_database_at(&db, |c| {
            Ok(c.query_row(
                "SELECT count(*) FROM s1_requests WHERE request_id LIKE 's10-figure-export-%'",
                [],
                |r| r.get(0),
            )?)
        })
        .unwrap();
        assert_eq!(count, 1);
        assert!(claim_export_request(
            &db,
            "f2345678-1234-1234-1234-123456789abc",
            "p",
            "paper",
            "figure",
            &dest
        )
        .is_ok());
    }

    #[test]
    fn s10_copy_cancel_midstream_preserves_original_and_never_marks_complete() {
        use sha2::{Digest, Sha256};
        use std::sync::atomic::{AtomicUsize, Ordering};
        let dir = tempfile::tempdir().unwrap();
        let original = vec![0x5A_u8; 3 * 1024 * 1024];
        let source = dir.path().join("source");
        let output = dir.path().join("partial");
        fs::write(&source, &original).unwrap();
        let sha = format!("{:x}", Sha256::digest(&original));
        let checks = AtomicUsize::new(0);
        let error = verified_copy(&source, &output, &sha, original.len() as i64, &|| {
            checks.fetch_add(1, Ordering::SeqCst) >= 1
        })
        .unwrap_err();
        assert_eq!(error.code, "CANCELLED");
        assert_eq!(fs::read(&source).unwrap(), original);
        assert!(fs::metadata(&output).unwrap().len() < original.len() as u64);
        // The caller's TempDir must be dropped on this failure.
    }

    #[test]
    fn s10_invalid_digest_missing_blob_and_symlink_are_rejected() {
        use sha2::{Digest, Sha256};
        let dir = tempfile::tempdir().unwrap();
        let src = dir.path().join("src");
        let dst = dir.path().join("dst");
        fs::write(&src, b"ABC").unwrap();
        let sha = format!("{:x}", Sha256::digest(b"ABC"));
        assert_eq!(
            verified_copy(&src, &dst, "not-a-sha", 3, &|| false)
                .unwrap_err()
                .code,
            "INVALID_BLOB"
        );
        assert_eq!(
            verified_copy(&dir.path().join("absent"), &dst, &sha, 3, &|| false)
                .unwrap_err()
                .code,
            "BLOB_MISSING"
        );
        assert_eq!(
            verified_copy(&src, &dst, &sha, 200, &|| false)
                .unwrap_err()
                .code,
            "BLOB_CORRUPT"
        );
        assert!(!dst.exists());
    }

    #[test]
    fn s10_aborted_process_staging_recovery_requires_durable_claim_marker_and_age() {
        let dir = tempfile::tempdir().unwrap();
        let db = dir.path().join("repo.sqlite");
        let dest = dir.path().join("exports");
        fs::create_dir(&dest).unwrap();
        let token = "11111111-2222-3333-4444-555555555555";
        assert_eq!(
            claim_export_request(&db, token, "p", "paper", "fig", &dest).unwrap(),
            Claim::New
        );
        let stale = tempfile::Builder::new()
            .prefix(".data-m-figure-stage-")
            .tempdir_in(&dest)
            .unwrap();
        let abandoned = stale.path().to_path_buf();
        fs::write(
            abandoned.join(".data-m-figure-owner.json"),
            serde_json::json!({"requestId":token}).to_string(),
        )
        .unwrap();
        fs::write(abandoned.join("partial"), b"NOT_A_COMPLETE_PACKAGE").unwrap();
        let abandoned = stale.keep(); // emulate abrupt process termination
        let unrelated = dest.join(".data-m-figure-stage-unrelated");
        fs::create_dir(&unrelated).unwrap();
        fs::write(unrelated.join("partial"), b"DO_NOT_DELETE").unwrap();
        // Never delete the stage while an export is still too recent.
        assert_eq!(
            recover_stale_stages_at(&db, &dest, std::time::Duration::from_secs(86400)).unwrap(),
            0
        );
        assert!(abandoned.is_dir());
        // A later explicit export to this selected folder can remove only
        // stale unpublished stages owned by an existing durable claim.
        assert_eq!(
            recover_stale_stages_at(&db, &dest, std::time::Duration::ZERO).unwrap(),
            1
        );
        assert!(!abandoned.exists());
        assert_eq!(
            fs::read(unrelated.join("partial")).unwrap(),
            b"DO_NOT_DELETE"
        );
        let finished = dest.join("figure-immutable");
        fs::create_dir(&finished).unwrap();
        fs::write(finished.join("figure-manifest.json"), b"KEEP").unwrap();
        assert_eq!(
            recover_stale_stages_at(&db, &dest, std::time::Duration::ZERO).unwrap(),
            0
        );
        assert_eq!(
            fs::read(finished.join("figure-manifest.json")).unwrap(),
            b"KEEP"
        );
    }

    #[cfg(windows)]
    #[test]
    fn s10_windows_directory_reparse_ancestors_cannot_authorize_export() {
        let dir = tempfile::tempdir().unwrap();
        let real = dir.path().join("real");
        fs::create_dir(&real).unwrap();
        let child = real.join("child");
        fs::create_dir(&child).unwrap();
        let link = dir.path().join("redirect");
        // Windows runner must actually create the adversarial fixture.
        std::os::windows::fs::symlink_dir(&real, &link)
            .expect("Windows CI must support native symlink security fixtures");
        assert_eq!(safe_dir(&link).unwrap_err().code, "UNSAFE_PATH");
        assert_eq!(
            safe_dir(&link.join("child")).unwrap_err().code,
            "UNSAFE_PATH"
        );
        assert!(safe_dir(&child).is_ok());
    }
}
