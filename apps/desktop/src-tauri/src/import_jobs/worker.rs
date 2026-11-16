use super::*;
use sha2::{Digest, Sha256};
use std::io::{Read, Write};

struct StagingWriter {
    file: std::fs::File,
    #[cfg(test)]
    fault: Option<Arc<dyn Fn(&str) -> Result<(), TreeError> + Send + Sync>>,
}
impl Write for StagingWriter {
    fn write(&mut self, bytes: &[u8]) -> std::io::Result<usize> {
        // Exercise the same Write::write_all error boundary as a full disk. The
        // fault injector and its callback are absent from non-test builds.
        #[cfg(test)]
        if let Some(fault) = &self.fault {
            if fault("write").is_err() {
                return Err(std::io::Error::from_raw_os_error(112));
            }
        }
        self.file.write(bytes)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        self.file.flush()
    }
}

#[derive(Clone)]
struct Intent {
    id: String,
    job: String,
    source: PathBuf,
    identity: String,
    mtime: String,
    size: i64,
    name: String,
    staging: String,
    staging_identity: Option<String>,
    hash: Option<String>,
    phase: String,
    asset: String,
    version: String,
    link: String,
}

fn files(repo: &Repository, job: Option<&str>) -> Result<Vec<Intent>, TreeError> {
    with_database_at(&repo.database, |c| {
        let mut statement=c.prepare("SELECT id,job_id,source_path,source_identity,source_mtime,size_bytes,name,staging_name,staging_identity,sha256,phase,asset_id,version_id,link_id FROM import_files WHERE (?1 IS NULL OR job_id=?1) ORDER BY job_id,ordinal")?;
        let rows = statement
            .query_map([job], |r| {
                Ok(Intent {
                    id: r.get(0)?,
                    job: r.get(1)?,
                    source: PathBuf::from(r.get::<_, String>(2)?),
                    identity: r.get(3)?,
                    mtime: r.get(4)?,
                    size: r.get(5)?,
                    name: r.get(6)?,
                    staging: r.get(7)?,
                    staging_identity: r.get(8)?,
                    hash: r.get(9)?,
                    phase: r.get(10)?,
                    asset: r.get(11)?,
                    version: r.get(12)?,
                    link: r.get(13)?,
                })
            })?
            .collect::<rusqlite::Result<Vec<_>>>()?;
        Ok(rows)
    })
}

fn cancelled(repo: &Repository, job: &str) -> Result<bool, TreeError> {
    with_database_at(&repo.database, |c| {
        Ok(c.query_row(
            "SELECT cancel_requested FROM import_jobs WHERE id=?1",
            [job],
            |r| r.get::<_, i64>(0),
        )? != 0)
    })
}

fn emit(repo: &Repository, job: &str, events: &Option<Events>) {
    if let Some(events) = events {
        if let Ok(view) = views(repo, Some(job)) {
            events(view);
        }
    }
}

struct Lease {
    repo: Arc<Repository>,
    job: String,
}
impl Drop for Lease {
    fn drop(&mut self) {
        if let Ok(mut active) = self.repo.active.lock() {
            active.remove(&self.job);
            self.repo.finished.notify_all();
        }
    }
}

pub(super) fn start(
    repo: &Arc<Repository>,
    job: &str,
    retry: bool,
    events: Option<Events>,
) -> Result<(), TreeError> {
    let mut active = repo.active.lock().map_err(|_| failure("REPOSITORY_BUSY"))?;
    if active.contains(job) {
        return Ok(());
    }
    let claimed = with_database_at(&repo.database, |c| {
        let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        let status = job_status(&tx, job)?;
        let eligible = if retry {
            matches!(status.as_str(), "failed" | "cancelled" | "interrupted")
        } else {
            status == "prepared"
        };
        if !eligible {
            return Ok(false);
        }
        tx.execute("UPDATE import_jobs SET status='running',cancel_requested=0,error_code=NULL WHERE id=?1",[job])?;
        tx.execute("UPDATE import_files SET status=phase,error_code=NULL WHERE job_id=?1 AND phase!='committed'",[job])?;
        tx.commit()?;
        Ok(true)
    })?;
    if !claimed {
        return Ok(());
    }
    active.insert(job.to_string());
    // If OS thread creation fails, dropping its captured Lease must be able to
    // acquire this mutex as well. Publish ownership before releasing the lock.
    drop(active);
    let lease = Lease {
        repo: repo.clone(),
        job: job.to_string(),
    };
    let job_id = job.to_string();
    let owned = repo.clone();
    let spawn = std::thread::Builder::new()
        .name("data-m-test-import".into())
        .spawn(move || {
            let _lease = lease;
            let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                run(&owned, &job_id, &events)
            }));
            match result {
                Ok(Ok(())) => {}
                Ok(Err(error)) => {
                    let _ = fail_job(&owned, &job_id, error.code);
                }
                Err(_) => {
                    let _ = fail_job(&owned, &job_id, "DATABASE_UNAVAILABLE");
                }
            }
            emit(&owned, &job_id, &events);
        });
    // The worker may finish immediately, so release this mutex before joining or
    // handling failure. Lease drops release active ownership on every exit path.
    if spawn.is_err() {
        fail_job(repo, job, "DATABASE_UNAVAILABLE")?;
        return Err(failure("DATABASE_UNAVAILABLE"));
    }
    Ok(())
}

fn fail_job(repo: &Repository, job: &str, code: &'static str) -> Result<(), TreeError> {
    with_database_at(&repo.database, |c| {
        let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        tx.execute("UPDATE import_jobs SET status='failed',error_code=?1 WHERE id=?2 AND status!='integrity_failed'",params![code,job])?;
        tx.execute("UPDATE import_files SET status='failed',error_code=?1 WHERE job_id=?2 AND phase!='committed'",params![code,job])?;
        tx.commit()?;
        Ok(())
    })
}

fn mark_file(repo: &Repository, file: &Intent, code: &'static str) -> Result<(), TreeError> {
    with_database_at(&repo.database, |c| {
        c.execute(
            "UPDATE import_files SET status=?1,error_code=?2 WHERE id=?3 AND phase!='committed'",
            params![
                if code == "CANCELLED" {
                    "cancelled"
                } else {
                    "failed"
                },
                code,
                file.id
            ],
        )?;
        Ok(())
    })
}

fn finish(repo: &Repository, job: &str, interrupted: bool) -> Result<(), TreeError> {
    with_database_at(&repo.database, |c| {
        let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        if job_status(&tx, job)? == "integrity_failed" {
            return Ok(());
        }
        let (remaining,failed):(i64,i64)=tx.query_row("SELECT count(*),COALESCE(sum(status='failed'),0) FROM import_files WHERE job_id=?1 AND phase!='committed'",[job],|r|Ok((r.get(0)?,r.get(1)?)))?;
        let cancel: i64 = tx.query_row(
            "SELECT cancel_requested FROM import_jobs WHERE id=?1",
            [job],
            |r| r.get(0),
        )?;
        let (status, code) = if remaining == 0 {
            ("completed", None)
        } else if cancel != 0 {
            ("cancelled", Some("CANCELLED"))
        } else if failed != 0 {
            ("failed", Some("FILE_FAILED"))
        } else if interrupted {
            ("interrupted", Some("INTERRUPTED"))
        } else {
            ("failed", Some("FILE_FAILED"))
        };
        tx.execute(
            "UPDATE import_jobs SET status=?1,error_code=?2 WHERE id=?3",
            params![status, code, job],
        )?;
        if cancel != 0 {
            tx.execute("UPDATE import_files SET status='cancelled',error_code='CANCELLED' WHERE job_id=?1 AND phase!='committed'",[job])?;
        }
        tx.commit()?;
        Ok(())
    })
}

fn run(repo: &Repository, job: &str, events: &Option<Events>) -> Result<(), TreeError> {
    for file in files(repo, Some(job))? {
        if file.phase == "committed" {
            continue;
        }
        if cancelled(repo, job)? {
            break;
        }
        if let Err(error) = process(repo, file.clone()) {
            mark_file(repo, &file, error.code)?;
            if error.code == "CANCELLED" {
                break;
            }
        }
        emit(repo, job, events);
    }
    finish(repo, job, false)
}

fn safe_staging(repo: &Repository, name: &str) -> Result<PathBuf, TreeError> {
    if name.len() != 32
        || !name
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err(failure("UNSAFE_PATH"));
    }
    Ok(repo.staging.join(name))
}

fn copy(repo: &Repository, file: &mut Intent) -> Result<(), TreeError> {
    let mut source = filesystem::source(&file.source, &repo.root)?;
    if source.info.id != file.identity
        || source.info.size != file.size
        || source.info.mtime != file.mtime
    {
        return Err(failure("SOURCE_CHANGED"));
    }
    if cancelled(repo, &file.job)? {
        return Err(failure("CANCELLED"));
    }
    // A retry never truncates/reuses an unverified partial staging file.
    file.staging = with_database_at(&repo.database, |c| {
        let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        let name: String = tx.query_row("SELECT lower(hex(randomblob(16)))", [], |r| r.get(0))?;
        tx.execute("UPDATE import_files SET staging_name=?1,staging_identity=NULL,sha256=NULL,phase='copying',status='copying',copied_bytes=0,error_code=NULL WHERE id=?2",params![name,file.id])?;
        tx.commit()?;
        Ok(name)
    })?;
    let path = safe_staging(repo, &file.staging)?;
    let mut output = StagingWriter {
        file: filesystem::stage_writer(repo, &file.staging)?,
        #[cfg(test)]
        fault: repo
            .hook
            .lock()
            .map_err(|_| failure("DATABASE_UNAVAILABLE"))?
            .clone(),
    };
    let stage_identity = filesystem::identity(&output.file)?.id;
    let mut sha = Sha256::new();
    let mut copied = 0_i64;
    let mut buffer = vec![0u8; 1024 * 1024];
    repo.checkpoint("copying")?;
    loop {
        if cancelled(repo, &file.job)? {
            return Err(failure("CANCELLED"));
        }
        let length = source
            .file
            .read(&mut buffer)
            .map_err(|_| failure("SOURCE_UNAVAILABLE"))?;
        if length == 0 {
            break;
        }
        repo.checkpoint("copying_chunk")?;
        output
            .write_all(&buffer[..length])
            .map_err(|_| failure("DISK_WRITE_FAILED"))?;
        sha.update(&buffer[..length]);
        copied += length as i64;
        with_database_at(&repo.database, |c| {
            c.execute(
                "UPDATE import_files SET copied_bytes=?1 WHERE id=?2",
                params![copied, file.id],
            )?;
            Ok(())
        })?;
    }
    if copied != file.size || filesystem::identity(&source.file)? != source.info {
        return Err(failure("SOURCE_CHANGED"));
    }
    output
        .file
        .sync_all()
        .map_err(|_| failure("DISK_WRITE_FAILED"))?;
    drop(output);
    repo.checkpoint("stage_closed")?;
    let stage = filesystem::stage_pin(repo, &file.staging)?;
    if filesystem::identity(&stage)?.id != stage_identity {
        return Err(failure("SOURCE_CHANGED"));
    }
    file.hash = Some(format!("{:x}", sha.finalize()));
    file.staging_identity = Some(stage_identity);
    // The protected staging handle remains held until the intent is durable.
    with_database_at(&repo.database, |c| {
        c.execute("UPDATE import_files SET phase='staged',status='staged',sha256=?1,staging_identity=?2,copied_bytes=size_bytes WHERE id=?3",params![file.hash,file.staging_identity,file.id])?;
        Ok(())
    })?;
    file.phase = "staged".into();
    repo.checkpoint("staged")?;
    // Publication happens while this same staging handle is still protected.
    publish(repo, file, stage)?;
    Ok(())
}

fn publish(repo: &Repository, file: &mut Intent, stage: std::fs::File) -> Result<(), TreeError> {
    if cancelled(repo, &file.job)? {
        return Err(failure("CANCELLED"));
    }
    let hash = file
        .hash
        .as_deref()
        .ok_or_else(|| failure("BLOB_INTEGRITY_FAILED"))?;
    if filesystem::identity(&stage)?.id
        != file
            .staging_identity
            .as_deref()
            .ok_or_else(|| failure("BLOB_INTEGRITY_FAILED"))?
    {
        return Err(failure("BLOB_INTEGRITY_FAILED"));
    }
    // Existing objects are verified rather than overwritten. Directory handles
    // prevent parent replacement; the staging handle prevents source replacement.
    let path = safe_staging(repo, &file.staging)?;
    if hash.len() != 64
        || !hash
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err(failure("BLOB_INTEGRITY_FAILED"));
    }
    filesystem::publish(repo, stage, hash)?;
    let object = filesystem::object(repo, hash, file.size)?;
    with_database_at(&repo.database, |c| {
        c.execute(
            "UPDATE import_files SET phase='blob_published',status='blob_published' WHERE id=?1",
            [&file.id],
        )?;
        Ok(())
    })?;
    file.phase = "blob_published".into();
    repo.checkpoint("published")?;
    commit(repo, file, &object)
}

fn process(repo: &Repository, mut file: Intent) -> Result<(), TreeError> {
    match file.phase.as_str() {
        "committed" => Ok(()),
        "blob_published" => {
            let object = filesystem::object(
                repo,
                file.hash
                    .as_deref()
                    .ok_or_else(|| failure("BLOB_INTEGRITY_FAILED"))?,
                file.size,
            )?;
            commit(repo, &file, &object)
        }
        "staged" => {
            safe_staging(repo, &file.staging)?;
            let mut stage = filesystem::stage_pin(repo, &file.staging)?;
            let actual = filesystem::digest(&mut stage)?;
            if file.hash.as_deref() != Some(actual.0.as_str()) || actual.1 != file.size {
                return Err(failure("BLOB_INTEGRITY_FAILED"));
            }
            publish(repo, &mut file, stage)
        }
        "queued" | "copying" => copy(repo, &mut file),
        _ => Err(failure("DATABASE_UNAVAILABLE")),
    }
}

fn commit(
    repo: &Repository,
    file: &Intent,
    _protected_object: &std::fs::File,
) -> Result<(), TreeError> {
    repo.checkpoint("before_commit")?;
    for directory in &repo._directories {
        directory.validate()?;
    }
    with_database_at(&repo.database, |c| {
        let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
        let cancel: i64 = tx.query_row(
            "SELECT cancel_requested FROM import_jobs WHERE id=?1",
            [&file.job],
            |r| r.get(0),
        )?;
        if cancel != 0 {
            return Err(failure("CANCELLED"));
        }
        let phase: String = tx.query_row(
            "SELECT phase FROM import_files WHERE id=?1",
            [&file.id],
            |r| r.get(0),
        )?;
        if phase == "committed" {
            return Ok(());
        }
        let target_json: String = tx.query_row(
            "SELECT target_json FROM import_jobs WHERE id=?1",
            [&file.job],
            |r| r.get(0),
        )?;
        let input: ImportTarget =
            serde_json::from_str(&target_json).map_err(|_| failure("DATABASE_UNAVAILABLE"))?;
        let target = target(&tx, &input)?;
        let conflict:bool=tx.query_row("SELECT EXISTS(SELECT 1 FROM asset_links WHERE assay_id=?1 AND COALESCE(group_id,'')=COALESCE(?2,'') AND COALESCE(replicate_id,'')=COALESCE(?3,'') AND stage=?4 AND display_name=?5 COLLATE NOCASE AND deleted_at IS NULL)",params![target.assay_id,target.group_id,target.replicate_id,target.stage,file.name],|r|r.get(0))?;
        if conflict {
            return Err(failure("NAME_CONFLICT"));
        }
        let hash = file
            .hash
            .as_deref()
            .ok_or_else(|| failure("BLOB_INTEGRITY_FAILED"))?;
        tx.execute(
            "INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2) ON CONFLICT(sha256) DO NOTHING",
            params![hash, file.size],
        )?;
        let size: i64 = tx.query_row(
            "SELECT size_bytes FROM blobs WHERE sha256=?1",
            [hash],
            |r| r.get(0),
        )?;
        if size != file.size {
            return Err(failure("BLOB_INTEGRITY_FAILED"));
        }
        tx.execute(
            "INSERT INTO data_assets(id,name) VALUES(?1,?2)",
            params![file.asset, file.name],
        )?;
        tx.execute("INSERT INTO asset_versions(id,asset_id,version_number,blob_sha256,source_name) VALUES(?1,?2,1,?3,?4)",params![file.version,file.asset,hash,file.name])?;
        tx.execute("INSERT INTO asset_links(id,asset_id,assay_id,group_id,replicate_id,stage,display_name) VALUES(?1,?2,?3,?4,?5,?6,?7)",params![file.link,file.asset,target.assay_id,target.group_id,target.replicate_id,target.stage,file.name])?;
        tx.execute("UPDATE import_files SET phase='committed',status='committed',error_code=NULL,copied_bytes=size_bytes WHERE id=?1",[&file.id])?;
        let remaining: i64 = tx.query_row(
            "SELECT count(*) FROM import_files WHERE job_id=?1 AND phase!='committed'",
            [&file.job],
            |r| r.get(0),
        )?;
        if remaining == 0 {
            tx.execute(
                "UPDATE import_jobs SET status='completed',error_code=NULL WHERE id=?1",
                [&file.job],
            )?;
        }
        tx.commit()?;
        Ok(())
    })?;
    repo.checkpoint("after_commit")?;
    Ok(())
}

pub(super) fn reconcile(repo: &Repository) -> Result<(), TreeError> {
    let files = files(repo, None)?;
    let mut jobs = HashSet::new();
    for file in files {
        jobs.insert(file.job.clone());
        if file.phase == "committed" {
            let verified = file
                .hash
                .as_deref()
                .ok_or_else(|| failure("BLOB_INTEGRITY_FAILED"))
                .and_then(|hash| filesystem::object(repo, hash, file.size));
            if verified.is_err() {
                with_database_at(&repo.database, |c| {
                    let tx = c.transaction_with_behavior(TransactionBehavior::Immediate)?;
                    tx.execute("UPDATE import_files SET status='committed',error_code='BLOB_INTEGRITY_FAILED' WHERE id=?1",[&file.id])?;
                    tx.execute("UPDATE import_jobs SET status='integrity_failed',error_code='BLOB_INTEGRITY_FAILED' WHERE id=?1",[&file.job])?;
                    tx.commit()?;
                    Ok(())
                })?;
            }
            continue;
        }
        let status = with_database_at(&repo.database, |c| job_status(c, &file.job))?;
        if !matches!(status.as_str(), "running" | "interrupted") {
            continue;
        }
        if cancelled(repo, &file.job)? {
            continue;
        }
        if matches!(file.phase.as_str(), "staged" | "blob_published") {
            if let Err(error) = process(repo, file.clone()) {
                mark_file(repo, &file, error.code)?;
            }
        } else {
            with_database_at(&repo.database, |c| {
                c.execute("UPDATE import_files SET status='interrupted',error_code='INTERRUPTED' WHERE id=?1",[&file.id])?;
                Ok(())
            })?;
        }
    }
    for job in jobs {
        let status = with_database_at(&repo.database, |c| job_status(c, &job))?;
        if matches!(status.as_str(), "running" | "interrupted") {
            finish(repo, &job, true)?;
        }
    }
    Ok(())
}
