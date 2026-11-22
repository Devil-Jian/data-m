//! S7 sealed snapshots. VACUUM INTO exports a consistent SQLite view even with
//! uncheckpointed WAL pages; never copy a live database file or its WAL sidecars.
use super::*;
use rusqlite::params;

fn emit(progress: &dyn Fn(SafetyProgress), phase: &str, completed: usize, total: usize) {
    progress(SafetyProgress {
        phase: phase.into(),
        completed,
        total,
    });
}
fn random_id(c: &Connection) -> Result<String, TreeError> {
    c.query_row("SELECT lower(hex(randomblob(16)))", [], |r| r.get(0))
        .map_err(map_read)
}
fn snapshot_root(base: &Path, id: &str) -> Result<PathBuf, TreeError> {
    if !valid_id(id) {
        return Err(failure("INVALID_REQUEST"));
    }
    Ok(base.join(id))
}
fn manifest_path(root: &Path) -> PathBuf {
    root.join("manifest.json")
}
fn unused_target(path: &Path) -> Result<bool, TreeError> {
    match fs::symlink_metadata(path) {
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(true),
        Ok(_) => Ok(false),
        Err(_) => Err(failure("UNSAFE_PATH")),
    }
}
fn summary(m: &Manifest) -> SnapshotSummary {
    SnapshotSummary {
        snapshot_id: m.snapshot_id.clone(),
        blob_count: m.blobs.len(),
        version_count: m.versions.len(),
        schema_version: m.schema_version,
        created_at: m.created_at.clone(),
    }
}
fn load_manifest(root: &Path, id: &str) -> Result<Manifest, TreeError> {
    safe_directory(root, false).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    let path = manifest_path(root);
    safe_regular(&path).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    let meta = fs::metadata(&path).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    if meta.len() > 32 * 1024 * 1024 {
        return Err(failure("SNAPSHOT_CORRUPT"));
    }
    let data = fs::read(&path).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    let m: Manifest = serde_json::from_slice(&data).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    if m.format_version != 1
        || m.snapshot_id != id
        || !matches!(m.schema_version, 2 | 3 | 4)
        || !valid_digest(&m.database_sha256)
        || m.database_size_bytes == 0
        || m.blobs.windows(2).any(|w| w[0].sha256 >= w[1].sha256)
        || m.versions.windows(2).any(|w| w[0].id >= w[1].id)
        || m.blobs
            .iter()
            .any(|b| !valid_digest(&b.sha256) || b.size_bytes < 0)
        || m.table_counts.len() != TABLES.len() + usize::from(m.schema_version >= 3) + usize::from(m.schema_version >= 4)
        || TABLES
            .iter()
            .chain(if m.schema_version >= 3 {
                S8_TABLES.iter()
            } else {
                [].iter()
            })
            .chain(if m.schema_version >= 4 {
                S14_TABLES.iter()
            } else {
                [].iter()
            })
            .any(|table| !m.table_counts.contains_key(*table))
    {
        return Err(failure("SNAPSHOT_CORRUPT"));
    }
    Ok(m)
}
fn assert_no_extra(root: &Path, manifest: &Manifest) -> Result<(), TreeError> {
    for item in fs::read_dir(root).map_err(|_| failure("SNAPSHOT_CORRUPT"))? {
        let entry = item.map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if name != "manifest.json" && name != "database.sqlite" && name != "objects" {
            return Err(failure("SNAPSHOT_CORRUPT"));
        }
    }
    let objects = root.join("objects");
    safe_directory(&objects, false).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    let registered = manifest
        .blobs
        .iter()
        .map(|b| b.sha256.as_str())
        .collect::<BTreeSet<_>>();
    for entry in fs::read_dir(&objects).map_err(|_| failure("SNAPSHOT_CORRUPT"))? {
        let entry = entry.map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if !registered.contains(name.as_str()) {
            return Err(failure("SNAPSHOT_CORRUPT"));
        }
        safe_regular(&entry.path()).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    }
    Ok(())
}
fn inspect_snapshot(
    root: &Path,
    m: &Manifest,
    progress: &dyn Fn(SafetyProgress),
) -> Result<SafetyReport, TreeError> {
    assert_no_extra(root, m)?;
    let database = root.join("database.sqlite");
    let (sha, size) = sha256_file(&database).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    if sha != m.database_sha256 || size != m.database_size_bytes {
        return Err(failure("SNAPSHOT_CORRUPT"));
    }
    let db = open_recognized(&database).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    let current = counts(&db).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    let snapshot_version: i64 = db
        .pragma_query_value(None, "user_version", |row| row.get(0))
        .map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    if snapshot_version != m.schema_version
        || current != m.table_counts
        || blob_list(&db).map_err(|_| failure("SNAPSHOT_CORRUPT"))? != m.blobs
        || version_list(&db).map_err(|_| failure("SNAPSHOT_CORRUPT"))? != m.versions
    {
        return Err(failure("SNAPSHOT_CORRUPT"));
    }
    let mut result = SafetyReport {
        table_counts: current,
        ..Default::default()
    };
    database_issues(&db, &mut result).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
    if !result.clean() {
        return Err(failure("SNAPSHOT_CORRUPT"));
    }
    let root_objects = root.join("objects");
    for (index, b) in m.blobs.iter().enumerate() {
        emit(progress, "verify_blobs", index, m.blobs.len());
        let (digest, size) =
            sha256_file(&root_objects.join(&b.sha256)).map_err(|_| failure("SNAPSHOT_CORRUPT"))?;
        if digest != b.sha256 || size != b.size_bytes as u64 {
            return Err(failure("SNAPSHOT_CORRUPT"));
        }
        result.checked_blobs += 1;
        result.verified_blobs += 1;
    }
    emit(progress, "verify_blobs", m.blobs.len(), m.blobs.len());
    Ok(result)
}
pub(super) fn verify_snapshot(
    base: &Path,
    id: &str,
    progress: &dyn Fn(SafetyProgress),
) -> Result<(Manifest, SafetyReport), TreeError> {
    if !safe_optional_dir(base)? {
        return Err(failure("SNAPSHOT_NOT_FOUND"));
    }
    safe_directory(base, false)?;
    let root = snapshot_root(base, id)?;
    if !safe_optional_dir(&root)? {
        return Err(failure("SNAPSHOT_NOT_FOUND"));
    }
    let m = load_manifest(&root, id)?;
    let report = inspect_snapshot(&root, &m, progress)?;
    Ok((m, report))
}
pub(super) fn list_snapshots(base: &Path) -> Result<Vec<SnapshotSummary>, TreeError> {
    if !safe_optional_dir(base)? {
        return Ok(Vec::new());
    }
    safe_directory(base, false)?;
    let mut out = Vec::new();
    for entry in fs::read_dir(base).map_err(map_read)? {
        let entry = entry.map_err(map_read)?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if !valid_id(&name) {
            continue;
        } // Unpublished recovery staging is never a snapshot.
        let manifest = load_manifest(&entry.path(), &name)?;
        out.push(summary(&manifest));
    }
    out.sort_by(|a, b| {
        b.created_at
            .cmp(&a.created_at)
            .then_with(|| b.snapshot_id.cmp(&a.snapshot_id))
    });
    Ok(out)
}
fn assert_quiescent(c: &Connection) -> Result<(), TreeError> {
    let pending: i64 = c
        .query_row(
            "SELECT count(*) FROM import_files WHERE phase!='committed'",
            [],
            |r| r.get(0),
        )
        .map_err(map_read)?;
    let running: i64 = c
        .query_row(
            "SELECT count(*) FROM import_jobs WHERE status IN ('prepared','running')",
            [],
            |r| r.get(0),
        )
        .map_err(map_read)?;
    if pending > 0 || running > 0 {
        return Err(failure("BACKUP_BUSY"));
    }
    Ok(())
}
#[cfg(test)]
pub(super) fn backup_with_hook(
    database: &Path,
    base: &Path,
    progress: &dyn Fn(SafetyProgress),
    hook: &dyn Fn(&str) -> Result<(), TreeError>,
) -> Result<Manifest, TreeError> {
    backup_inner(database, base, progress, hook)
}
pub(super) fn backup(
    database: &Path,
    base: &Path,
    progress: &dyn Fn(SafetyProgress),
) -> Result<Manifest, TreeError> {
    backup_inner(database, base, progress, &|_| Ok(()))
}
fn backup_inner(
    database: &Path,
    base: &Path,
    progress: &dyn Fn(SafetyProgress),
    hook: &dyn Fn(&str) -> Result<(), TreeError>,
) -> Result<Manifest, TreeError> {
    // Opening an unrecognized or nonexistent source never creates a workspace.
    let source = open_recognized(database)?;
    assert_quiescent(&source)?;
    let cas = database
        .parent()
        .ok_or_else(|| failure("UNSAFE_PATH"))?
        .join("cas");
    let report = inspect(&source, &cas)?;
    if !report.clean() {
        return Err(failure("INTEGRITY_FAILED"));
    }
    safe_directory(base, true)?;
    let id = random_id(&source)?;
    if !valid_id(&id) {
        return Err(failure("DISK_WRITE_FAILED"));
    }
    let final_path = snapshot_root(base, &id)?;
    if !unused_target(&final_path)? {
        return Err(failure("DISK_WRITE_FAILED"));
    }
    let stage = tempfile::Builder::new()
        .prefix(".s7-backup-")
        .tempdir_in(base)
        .map_err(map_write)?;
    let dbfile = stage.path().join("database.sqlite");
    emit(progress, "sqlite_snapshot", 0, 1);
    // Unlike plain filesystem copying, VACUUM INTO includes committed WAL pages.
    source
        .execute("VACUUM INTO ?1", params![dbfile.to_string_lossy().as_ref()])
        .map_err(map_write)?;
    emit(progress, "sqlite_snapshot", 1, 1);
    hook("after_sqlite_snapshot")?;
    let snapdb = open_recognized(&dbfile)?;
    assert_quiescent(&snapdb)?;
    let (database_sha256, database_size_bytes) = sha256_file(&dbfile)?;
    let table_counts = counts(&snapdb)?;
    let blobs = blob_list(&snapdb)?;
    let versions = version_list(&snapdb)?;
    // The source might change during VACUUM; the snapshot controls the manifest.
    // Verify each snapshot-registered CAS digest from the official S3 flat layout.
    let objects = stage.path().join("objects");
    fs::create_dir(&objects).map_err(map_write)?;
    safe_directory(&objects, false)?;
    let source_objects = cas.join("objects");
    for (index, blob) in blobs.iter().enumerate() {
        emit(progress, "copy_blobs", index, blobs.len());
        if !valid_digest(&blob.sha256) || blob.size_bytes < 0 {
            return Err(failure("INTEGRITY_FAILED"));
        }
        copy_verified(
            &source_objects.join(&blob.sha256),
            &objects.join(&blob.sha256),
            &blob.sha256,
            blob.size_bytes as u64,
        )?;
        hook("after_blob_copy")?;
    }
    emit(progress, "copy_blobs", blobs.len(), blobs.len());
    let created_at: String = snapdb
        .query_row("SELECT strftime('%Y-%m-%dT%H:%M:%fZ','now')", [], |r| {
            r.get(0)
        })
        .map_err(map_read)?;
    let m = Manifest {
        format_version: 1,
        snapshot_id: id,
        schema_version: snapdb
            .pragma_query_value(None, "user_version", |row| row.get(0))
            .map_err(map_read)?,
        database_sha256,
        database_size_bytes,
        created_at,
        table_counts,
        blobs,
        versions,
    };
    let serialized = serde_json::to_vec_pretty(&m).map_err(map_write)?;
    let mut manifest_file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(stage.path().join("manifest.json"))
        .map_err(map_write)?;
    manifest_file.write_all(&serialized).map_err(map_write)?;
    manifest_file.sync_all().map_err(map_write)?;
    drop(manifest_file);
    drop(snapdb);
    inspect_snapshot(stage.path(), &m, progress)?;
    hook("before_publish")?;
    if !unused_target(&final_path)? {
        return Err(failure("DISK_WRITE_FAILED"));
    }
    fs::rename(stage.path(), &final_path).map_err(map_write)?;
    emit(progress, "published", 1, 1);
    Ok(m)
}
pub(super) fn restore_snapshot(
    base: &Path,
    restore_base: &Path,
    id: &str,
    label: &str,
    progress: &dyn Fn(SafetyProgress),
) -> Result<SafetyReport, TreeError> {
    restore_inner(base, restore_base, id, label, progress, &|_| Ok(()))
}
#[cfg(test)]
pub(super) fn restore_with_hook(
    base: &Path,
    restore_base: &Path,
    id: &str,
    label: &str,
    progress: &dyn Fn(SafetyProgress),
    hook: &dyn Fn(&str) -> Result<(), TreeError>,
) -> Result<SafetyReport, TreeError> {
    restore_inner(base, restore_base, id, label, progress, hook)
}
fn restore_inner(
    base: &Path,
    restore_base: &Path,
    id: &str,
    label: &str,
    progress: &dyn Fn(SafetyProgress),
    hook: &dyn Fn(&str) -> Result<(), TreeError>,
) -> Result<SafetyReport, TreeError> {
    if !valid_id(id) || !valid_label(label) {
        return Err(failure("INVALID_REQUEST"));
    }
    // Full preflight before even creating the recovery directory.
    let (manifest, _) = verify_snapshot(base, id, progress)?;
    let target = restore_base.join(label);
    if !unused_target(&target)? {
        return Err(failure("RESTORE_CONFLICT"));
    }
    safe_directory(restore_base, true)?;
    let stage = tempfile::Builder::new()
        .prefix(".s7-restore-")
        .tempdir_in(restore_base)
        .map_err(map_write)?;
    let file = stage.path().join("project-tree.sqlite");
    emit(progress, "restore_sqlite", 0, 1);
    copy_verified(
        &base.join(id).join("database.sqlite"),
        &file,
        &manifest.database_sha256,
        manifest.database_size_bytes,
    )?;
    emit(progress, "restore_sqlite", 1, 1);
    hook("after_restore_sqlite")?;
    let objects = stage.path().join("cas").join("objects");
    fs::create_dir_all(&objects).map_err(map_write)?;
    let staging = stage.path().join("cas").join("staging");
    fs::create_dir(&staging).map_err(map_write)?;
    safe_directory(&objects, false)?;
    for (index, blob) in manifest.blobs.iter().enumerate() {
        emit(progress, "restore_blobs", index, manifest.blobs.len());
        copy_verified(
            &base.join(id).join("objects").join(&blob.sha256),
            &objects.join(&blob.sha256),
            &blob.sha256,
            blob.size_bytes as u64,
        )?;
        hook("after_restore_blob")?;
    }
    emit(
        progress,
        "restore_blobs",
        manifest.blobs.len(),
        manifest.blobs.len(),
    );
    // Restore provenance is a copy and is not treated as a new sealed snapshot.
    let mut mfile = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(stage.path().join("restored-from-snapshot.json"))
        .map_err(map_write)?;
    mfile
        .write_all(&serde_json::to_vec_pretty(&manifest).map_err(map_write)?)
        .map_err(map_write)?;
    mfile.sync_all().map_err(map_write)?;
    drop(mfile);
    let c = open_recognized(&file)?;
    let report = clean_dataset(&c, &stage.path().join("cas"))?;
    if counts(&c)? != manifest.table_counts
        || blob_list(&c)? != manifest.blobs
        || version_list(&c)? != manifest.versions
    {
        return Err(failure("SNAPSHOT_CORRUPT"));
    }
    drop(c);
    hook("before_restore_publish")?;
    if !unused_target(&target)? {
        return Err(failure("RESTORE_CONFLICT"));
    }
    fs::rename(stage.path(), &target).map_err(map_write)?;
    emit(progress, "restore_published", 1, 1);
    Ok(report)
}
