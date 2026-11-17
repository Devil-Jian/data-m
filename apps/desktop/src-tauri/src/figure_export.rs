//! S10 Figure byte-preserving exporter. Not exposed as an arbitrary-path IPC.
//! A future native picker must supply the explicitly authorized destination.
//! All source bytes are read from immutable, exact-version CAS objects.
use crate::project_tree::{required, with_database_at, TreeError};
use rusqlite::{params, Connection};
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
#[derive(Debug, Clone, Serialize)]
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
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ManifestAnalysis {
    pub analysis_id: String,
    pub role: String,
    pub asset_version_id: String,
    pub frozen_at: Option<String>,
    pub included_replicate_ids: Vec<String>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FigureManifest {
    pub manifest_version: u32,
    pub schema_version: u32,
    pub created_at: String,
    pub project_id: String,
    pub paper_id: String,
    pub figure_id: String,
    pub references: Vec<ManifestEntry>,
    pub analyses: Vec<ManifestAnalysis>,
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
    if !path.is_absolute() {
        return Err(error("UNSAFE_PATH"));
    }
    let meta = fs::symlink_metadata(path).map_err(|_| error("UNSAFE_PATH"))?;
    if !meta.file_type().is_dir() || meta.file_type().is_symlink() {
        return Err(error("UNSAFE_PATH"));
    }
    let canonical = fs::canonicalize(path).map_err(|_| error("UNSAFE_PATH"))?;
    // The destination must already exist and not be changed into a symlink.
    if fs::symlink_metadata(&canonical)
        .map_err(|_| error("UNSAFE_PATH"))?
        .file_type()
        .is_symlink()
    {
        return Err(error("UNSAFE_PATH"));
    }
    Ok(canonical)
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
    let _source_guard = crate::import_jobs::filesystem::Directories::hold(&cas_root, false)?;
    if destination.starts_with(&repository) || repository.starts_with(&destination) {
        return Err(error("UNSAFE_PATH"));
    }
    let (items, analyses, created_at) = with_database_at(database, |c| {
        let (items, analyses) = planned(c, project, paper, figure)?;
        let now: String = c.query_row("SELECT strftime('%Y-%m-%dT%H:%M:%fZ','now')", [], |r| {
            r.get(0)
        })?;
        Ok((items, analyses, now))
    })?;
    if items.is_empty() {
        return Err(error("EMPTY_FIGURE"));
    }
    let stage = tempfile::Builder::new()
        .prefix(".data-m-figure-stage-")
        .tempdir_in(&destination)
        .map_err(|_| error("DISK_WRITE_FAILED"))?;
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
        analyses,
        status: "complete".into(),
    };
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(stage.path().join("figure-manifest.json"))
        .map_err(|_| error("DISK_WRITE_FAILED"))?;
    serde_json::to_writer_pretty(&mut output, &manifest).map_err(|_| error("DISK_WRITE_FAILED"))?;
    output.sync_all().map_err(|_| error("DISK_WRITE_FAILED"))?;
    // Caller may provide only a previously existing destination directory.
    // Random subdirectory avoids name collision and never touches existing
    // files. Windows rename cannot replace an existing target directory.
    let random_id = with_database_at(database, |c| {
        c.query_row("SELECT lower(hex(randomblob(16)))", [], |r| {
            r.get::<_, String>(0)
        })
        .map_err(TreeError::from)
    })?;
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
    let staging_path = stage.keep();
    match fs::rename(&staging_path, &target) {
        Ok(()) => Ok((target, manifest)),
        Err(rename_error) => {
            #[cfg(test)]
            eprintln!("S10 export: publish OS error {:?}", rename_error);
            #[cfg(not(test))]
            let _ = &rename_error;
            let _ = fs::remove_dir_all(&staging_path);
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
            let destination = pick_native_folder()?;
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
            let (folder, manifest) = export_figure_with_hooks(
                &database,
                &request.project_id,
                &request.paper_id,
                &request.figure_id,
                &destination,
                &|| flag.load(Ordering::SeqCst),
                &progress,
            )?;
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
        let before_count = fs::read_dir(&dest).unwrap().count();
        assert_eq!(
            export_figure_with_hooks(&db, "p", "paper", "fig", &dest, &|| true, &|_, _| {})
                .unwrap_err()
                .code,
            "CANCELLED"
        );
        assert_eq!(fs::read_dir(&dest).unwrap().count(), before_count);
        fs::write(objects.join(&sha), vec![b'X'; original.len()]).unwrap();
        assert_eq!(
            export_figure_at(&db, "p", "paper", "fig", &dest)
                .unwrap_err()
                .code,
            "BLOB_CORRUPT"
        );
    }
}
