//! Disposable-workspace import jobs. Filesystem phases are persisted independently
//! from each file's atomic SQLite registration; no filesystem transaction is assumed.
use crate::project_tree::{database_path, required, required_name, with_database_at, TreeError};
use rusqlite::{params, Connection, OptionalExtension, TransactionBehavior};
use serde::{Deserialize, Serialize};
use std::{
    collections::{HashMap, HashSet},
    path::{Path, PathBuf},
    sync::{Arc, Condvar, Mutex, OnceLock, Weak},
};
use tauri::{Emitter, Manager};

pub(crate) mod filesystem;
mod worker;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ImportTarget {
    assay_id: String,
    stage: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    project_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    group_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    replicate_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ImportRequest {
    action: String,
    paths: Option<Vec<String>>,
    // Optional S13 user-reviewed destination names. Source names/bytes are unchanged.
    aliases: Option<Vec<String>>,
    target: Option<ImportTarget>,
    request_id: Option<String>,
    job_id: Option<String>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FileView {
    id: String,
    name: String,
    size_bytes: i64,
    sha256: Option<String>,
    status: String,
    asset_version_id: Option<String>,
    error_code: Option<String>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct JobView {
    id: String,
    status: String,
    target: ImportTarget,
    files: Vec<FileView>,
    total_bytes: i64,
    copied_bytes: i64,
    completed_count: usize,
    error_code: Option<String>,
}
#[derive(Debug, Clone, Serialize)]
pub struct JobsView {
    jobs: Vec<JobView>,
}

type Events = Arc<dyn Fn(JobsView) + Send + Sync>;
type RepositoryCache = Mutex<HashMap<PathBuf, Weak<Repository>>>;
static REPOSITORIES: OnceLock<RepositoryCache> = OnceLock::new();

#[derive(Default)]
struct ImportRuntime(Mutex<HashMap<PathBuf, Arc<Repository>>>);

struct Repository {
    database: PathBuf,
    root: PathBuf,
    staging: PathBuf,
    objects: PathBuf,
    _directories: Vec<filesystem::Directories>,
    _lock: std::fs::File,
    active: Mutex<HashSet<String>>,
    finished: Condvar,
    #[cfg(test)]
    hook: Mutex<Option<Arc<dyn Fn(&str) -> Result<(), TreeError> + Send + Sync>>>,
}

impl Repository {
    fn acquire(database: &Path) -> Result<Arc<Self>, TreeError> {
        let parent = database.parent().ok_or_else(|| failure("UNSAFE_PATH"))?;
        let directories = filesystem::Directories::hold(parent, false)?;
        // Bind the existing S1/S2 DB and every ancestor before path-based SQLite
        // access. The readonly pin prevents removing the last directory entry.
        let db_pin = filesystem::database_pin(
            &directories,
            database
                .file_name()
                .and_then(|x| x.to_str())
                .ok_or_else(|| failure("UNSAFE_PATH"))?,
        )?;
        directories.validate()?;
        crate::project_tree::validate_readonly(database)?;
        let database =
            std::fs::canonicalize(database).map_err(|_| failure("DATABASE_UNAVAILABLE"))?;
        let mut cache = REPOSITORIES
            .get_or_init(|| Mutex::new(HashMap::new()))
            .lock()
            .map_err(|_| failure("REPOSITORY_BUSY"))?;
        if let Some(existing) = cache.get(&database).and_then(Weak::upgrade) {
            return Ok(existing);
        }
        let root = database
            .parent()
            .ok_or_else(|| failure("UNSAFE_PATH"))?
            .to_path_buf();
        let lock = filesystem::repository_lock(&directories)?;
        directories.validate()?;
        drop(db_pin);
        with_database_at(&database, |_| Ok(()))?;
        let cas = root.join("cas");
        let staging = cas.join("staging");
        let objects = cas.join("objects");
        let mut cas_dirs = filesystem::Directories::hold(&cas, true)?;
        cas_dirs.pin()?;
        let mut stage_dirs = filesystem::Directories::hold(&staging, true)?;
        stage_dirs.pin()?;
        let mut object_dirs = filesystem::Directories::hold(&objects, true)?;
        object_dirs.pin()?;
        let protected = vec![directories, cas_dirs, stage_dirs, object_dirs];
        let repo = Arc::new(Self {
            database: database.clone(),
            root,
            staging,
            objects,
            _directories: protected,
            _lock: lock,
            active: Mutex::new(HashSet::new()),
            finished: Condvar::new(),
            #[cfg(test)]
            hook: Mutex::new(None),
        });
        worker::reconcile(&repo)?;
        cache.insert(database, Arc::downgrade(&repo));
        Ok(repo)
    }
    fn is_active(&self, id: &str) -> bool {
        self.active.lock().map(|a| a.contains(id)).unwrap_or(true)
    }
    fn checkpoint(&self, _phase: &str) -> Result<(), TreeError> {
        #[cfg(test)]
        {
            let callback = self
                .hook
                .lock()
                .map_err(|_| failure("DATABASE_UNAVAILABLE"))?
                .clone();
            if let Some(callback) = callback {
                return callback(_phase);
            }
        }
        Ok(())
    }
    #[cfg(test)]
    fn set_hook(&self, hook: Arc<dyn Fn(&str) -> Result<(), TreeError> + Send + Sync>) {
        *self.hook.lock().unwrap() = Some(hook);
    }
}

fn failure(code: &'static str) -> TreeError {
    let message = match code {
        "INVALID_TARGET" => {
            "Select an active sample for Raw or Processed, or an assay for Statistics or Display."
        }
        "SOURCE_CHANGED" => "A source file changed after preparation. Prepare a new import.",
        "SOURCE_UNAVAILABLE" => "A source file is unavailable or locked.",
        "NAME_CONFLICT" => "A file with this name already exists in the selected container.",
        "UNSAFE_PATH" => {
            "Only ordinary files outside the controlled test repository can be imported."
        }
        "REPOSITORY_BUSY" => "The test repository is busy in another process. Please retry.",
        "BLOB_INTEGRITY_FAILED" => {
            "Stored bytes could not be verified. The import requires an integrity review."
        }
        "DISK_WRITE_FAILED" => {
            "The test import could not write its staged bytes. Check available disk space."
        }
        "REQUEST_CONFLICT" => "This request ID was already used for a different operation.",
        "NOT_FOUND" => "The import job no longer exists.",
        "CANCELLED" => "The import was cancelled. Committed files were retained.",
        "INTERRUPTED" => "The import was interrupted. Unfinished files can be retried.",
        "UNSUPPORTED_PLATFORM" => "Protected test imports require Windows.",
        _ => "The test import could not be completed. Please retry.",
    };
    TreeError::new(code, message)
}

#[tauri::command]
pub async fn import_jobs<R: tauri::Runtime>(
    app: tauri::AppHandle<R>,
    request: ImportRequest,
) -> Result<JobsView, TreeError> {
    validate(&request)?;
    let path = database_path(&app)?;
    tauri::async_runtime::spawn_blocking(move || {
        if app.try_state::<ImportRuntime>().is_none() {
            app.manage(ImportRuntime::default());
        }
        let runtime = app.state::<ImportRuntime>();
        let repo = {
            let mut services = runtime.0.lock().map_err(|_| failure("REPOSITORY_BUSY"))?;
            if let Some(repo) = services.get(&path) {
                repo.clone()
            } else {
                let repo = Repository::acquire(&path)?;
                services.insert(path.clone(), repo.clone());
                repo
            }
        };
        let event_app = app.clone();
        let events: Events = Arc::new(move |view| {
            let _ = event_app.emit("data-m-import-progress", view);
        });
        execute(&repo, request, Some(events))
    })
    .await
    .map_err(|_| failure("DATABASE_UNAVAILABLE"))?
}

fn validate(request: &ImportRequest) -> Result<(), TreeError> {
    match request.action.as_str() {
        "prepare" => {
            required(request.request_id.as_deref(), 128)?;
            if request.job_id.is_some() || request.target.is_none() {
                return Err(TreeError::input());
            }
            let paths = request
                .paths
                .as_ref()
                .filter(|p| !p.is_empty() && p.len() <= 1000)
                .ok_or_else(TreeError::input)?;
            for path in paths {
                required(Some(path), 32767)?;
            }
            if let Some(aliases)=request.aliases.as_ref(){
                if aliases.len()!=paths.len(){return Err(TreeError::input());}
                for alias in aliases {
                    required_name(Some(alias)).map_err(|_|failure("INVALID_REQUEST"))?;
                    if alias.contains('/')||alias.contains('\\')||alias.contains(':')
                        || alias=="." || alias==".." {return Err(failure("INVALID_REQUEST"))}
                }
            }
        }
        "start" | "get" | "cancel" | "retry" => {
            required(request.job_id.as_deref(), 128)?;
            if request.paths.is_some() || request.aliases.is_some() || request.target.is_some() || request.request_id.is_some() {
                return Err(TreeError::input());
            }
        }
        "list" => {
            if request.paths.is_some()
                || request.aliases.is_some()
                || request.target.is_some()
                || request.request_id.is_some()
                || request.job_id.is_some()
            {
                return Err(TreeError::input());
            }
        }
        _ => return Err(TreeError::input()),
    }
    Ok(())
}

fn execute(
    repo: &Arc<Repository>,
    request: ImportRequest,
    events: Option<Events>,
) -> Result<JobsView, TreeError> {
    validate(&request)?;
    let id = match request.action.as_str() {
        "prepare" => Some(prepare_job(repo, &request)?),
        "start" | "retry" => {
            let id = required(request.job_id.as_deref(), 128)?.to_string();
            worker::start(repo, &id, request.action == "retry", events.clone())?;
            Some(id)
        }
        "cancel" => {
            let id = required(request.job_id.as_deref(), 128)?.to_string();
            with_database_at(&repo.database, |c| {
                let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
                let status = job_status(&tx, &id)?;
                if !matches!(status.as_str(), "completed" | "integrity_failed") {
                    let remaining: i64 = tx.query_row(
                        "SELECT count(*) FROM import_files WHERE job_id=?1 AND phase!='committed'",
                        [&id],
                        |r| r.get(0),
                    )?;
                    if remaining == 0 {
                        tx.execute(
                            "UPDATE import_jobs SET status='completed',error_code=NULL WHERE id=?1",
                            [&id],
                        )?;
                    } else {
                        tx.execute("UPDATE import_jobs SET cancel_requested=1,status='cancelled',error_code='CANCELLED' WHERE id=?1",[&id])?;
                        tx.execute("UPDATE import_files SET status='cancelled',error_code='CANCELLED' WHERE job_id=?1 AND phase!='committed'",[&id])?;
                    }
                }
                tx.commit()?;
                Ok(())
            })?;
            Some(id)
        }
        "get" => request.job_id.clone(),
        "list" => None,
        _ => return Err(TreeError::input()),
    };
    let view = views(repo, id.as_deref())?;
    if let Some(events) = events {
        events(view.clone());
    }
    Ok(view)
}

fn target(connection: &Connection, input: &ImportTarget) -> Result<ImportTarget, TreeError> {
    if matches!(input.stage.as_str(), "paper" | "figure") {
        if !input.assay_id.is_empty() || input.group_id.is_some() || input.replicate_id.is_some() {
            return Err(failure("INVALID_TARGET"));
        }
        let project = required(input.project_id.as_deref(), 128)
            .map_err(|_|failure("INVALID_TARGET"))?;
        let active:bool=connection.query_row(
            "SELECT EXISTS(SELECT 1 FROM projects WHERE id=?1 AND deleted_at IS NULL)",
            [project],|r|r.get(0))?;
        return if active {Ok(input.clone())} else {Err(failure("INVALID_TARGET"))};
    }
    if input.project_id.is_some() {return Err(failure("INVALID_TARGET"))}
    required(Some(&input.assay_id), 128).map_err(|_| failure("INVALID_TARGET"))?;
    let active:bool=connection.query_row("SELECT EXISTS(SELECT 1 FROM assays a JOIN themes t ON t.id=a.theme_id JOIN projects p ON p.id=t.project_id WHERE a.id=?1 AND a.deleted_at IS NULL AND t.deleted_at IS NULL AND p.deleted_at IS NULL)",[&input.assay_id],|r|r.get(0))?;
    if !active {
        return Err(failure("INVALID_TARGET"));
    }
    let mut output = input.clone();
    match input.stage.as_str() {
        "raw" | "processed" => {
            let sample = required(input.replicate_id.as_deref(), 128)
                .map_err(|_| failure("INVALID_TARGET"))?;
            let group:Option<String>=connection.query_row("SELECT g.id FROM replicates r JOIN experiment_groups g ON g.id=r.group_id WHERE r.id=?1 AND g.assay_id=?2 AND r.deleted_at IS NULL AND g.deleted_at IS NULL",params![sample,input.assay_id],|r|r.get(0)).optional()?;
            let group = group.ok_or_else(|| failure("INVALID_TARGET"))?;
            if input.group_id.as_ref().is_some_and(|id| id != &group) {
                return Err(failure("INVALID_TARGET"));
            }
            output.group_id = Some(group);
        }
        "statistics" | "display" => {
            if input.group_id.is_some() || input.replicate_id.is_some() {
                return Err(failure("INVALID_TARGET"));
            }
        }
        _ => return Err(failure("INVALID_TARGET")),
    }
    Ok(output)
}

fn prepare_job(repo: &Repository, request: &ImportRequest) -> Result<String, TreeError> {
    let token = required(request.request_id.as_deref(), 128)?;
    let payload = serde_json::to_string(request).map_err(|_| TreeError::input())?;
    let existing = with_database_at(&repo.database, |c| replay(c, token, &payload))?;
    if let Some(id) = existing {
        return Ok(id);
    }
    let paths = request.paths.as_ref().ok_or_else(TreeError::input)?;
    let mut inspected = Vec::new();
    let mut total = 0_i64;
    for path in paths {
        let source = filesystem::source(Path::new(path), &repo.root)?;
        total = total
            .checked_add(source.info.size)
            .ok_or_else(TreeError::input)?;
        inspected.push(source);
    }
    with_database_at(&repo.database, |c| {
        let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        if let Some(id) = replay(&tx, token, &payload)? {
            return Ok(id);
        }
        let resolved = target(&tx, request.target.as_ref().ok_or_else(TreeError::input)?)?;
        let target_json = serde_json::to_string(&resolved).map_err(|_| TreeError::input())?;
        let job: String = tx.query_row("SELECT lower(hex(randomblob(16)))", [], |r| r.get(0))?;
        tx.execute("INSERT INTO import_jobs(id,request_id,payload,target_json,status) VALUES(?1,?2,?3,?4,'prepared')",params![job,token,payload,target_json])?;
        for (ordinal, source) in inspected.iter().enumerate() {
            let path = source.path.to_str().ok_or_else(|| failure("UNSAFE_PATH"))?;
            let name = request.aliases.as_ref().map(|a|a[ordinal].as_str())
                .or_else(||Path::new(&paths[ordinal]).file_name().and_then(|n|n.to_str()))
                .ok_or_else(|| failure("UNSAFE_PATH"))?;
            tx.execute("INSERT INTO import_files(id,job_id,ordinal,source_path,source_identity,source_mtime,size_bytes,name,staging_name,phase,status,asset_id,version_id,link_id) VALUES(lower(hex(randomblob(16))),?1,?2,?3,?4,?5,?6,?7,lower(hex(randomblob(16))),'queued','queued',lower(hex(randomblob(16))),lower(hex(randomblob(16))),lower(hex(randomblob(16))))",params![job,ordinal as i64,path,source.info.id,source.info.mtime,source.info.size,name])?;
        }
        tx.commit()?;
        Ok(job)
    })
}

fn replay(
    connection: &Connection,
    token: &str,
    payload: &str,
) -> Result<Option<String>, TreeError> {
    let previous: Option<(String, String)> = connection
        .query_row(
            "SELECT id,payload FROM import_jobs WHERE request_id=?1",
            [token],
            |r| Ok((r.get(0)?, r.get(1)?)),
        )
        .optional()?;
    match previous {
        Some((_, previous)) if previous != payload => Err(failure("REQUEST_CONFLICT")),
        Some((id, _)) => Ok(Some(id)),
        None => Ok(None),
    }
}

fn job_status(connection: &Connection, id: &str) -> Result<String, TreeError> {
    connection
        .query_row("SELECT status FROM import_jobs WHERE id=?1", [id], |r| {
            r.get(0)
        })
        .optional()?
        .ok_or_else(|| failure("NOT_FOUND"))
}

fn views(repo: &Repository, id: Option<&str>) -> Result<JobsView, TreeError> {
    with_database_at(&repo.database, |c| {
        let tx = c.transaction()?;
        if let Some(id) = id {
            job_status(&tx, id)?;
        }
        let mut jobs = Vec::new();
        let mut statement=tx.prepare("SELECT id,status,target_json,error_code FROM import_jobs WHERE (?1 IS NULL OR id=?1) ORDER BY created_at,id")?;
        let rows = statement
            .query_map([id], |r| {
                Ok((
                    r.get::<_, String>(0)?,
                    r.get::<_, String>(1)?,
                    r.get::<_, String>(2)?,
                    r.get::<_, Option<String>>(3)?,
                ))
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        drop(statement);
        for (id, status, target_json, error_code) in rows {
            let target =
                serde_json::from_str(&target_json).map_err(|_| failure("DATABASE_UNAVAILABLE"))?;
            let mut statement=tx.prepare("SELECT id,name,size_bytes,sha256,status,CASE WHEN phase='committed' THEN version_id END,error_code,copied_bytes,phase FROM import_files WHERE job_id=?1 ORDER BY ordinal")?;
            let mut total_bytes = 0;
            let mut copied_bytes = 0;
            let mut completed_count = 0;
            let files = statement
                .query_map([&id], |r| {
                    let size: i64 = r.get(2)?;
                    let copied: i64 = r.get(7)?;
                    let phase: String = r.get(8)?;
                    total_bytes += size;
                    copied_bytes += copied;
                    if phase == "committed" {
                        completed_count += 1;
                    }
                    Ok(FileView {
                        id: r.get(0)?,
                        name: r.get(1)?,
                        size_bytes: size,
                        sha256: r.get(3)?,
                        status: r.get(4)?,
                        asset_version_id: r.get(5)?,
                        error_code: r.get(6)?,
                    })
                })?
                .collect::<rusqlite::Result<Vec<_>>>()?;
            jobs.push(JobView {
                id,
                status,
                target,
                files,
                total_bytes,
                copied_bytes,
                completed_count,
                error_code,
            });
        }
        tx.commit()?;
        Ok(JobsView { jobs })
    })
}

#[cfg(all(test, windows))]
#[path = "import_jobs/tests.rs"]
mod tests;
