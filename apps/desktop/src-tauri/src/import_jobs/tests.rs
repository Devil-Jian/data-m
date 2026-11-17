use super::*;
use serde_json::json;
use std::io::Write;
use std::time::{Duration, Instant};

#[cfg(windows)]
fn set_junction_in_place(link: &Path, target: &Path) -> std::io::Result<()> {
    use std::os::windows::fs::OpenOptionsExt;
    use std::os::windows::io::AsRawHandle;

    #[link(name = "kernel32")]
    extern "system" {
        fn DeviceIoControl(
            device: *mut std::ffi::c_void,
            control_code: u32,
            input: *const std::ffi::c_void,
            input_size: u32,
            output: *mut std::ffi::c_void,
            output_size: u32,
            returned: *mut u32,
            overlapped: *mut std::ffi::c_void,
        ) -> i32;
    }

    let handle = std::fs::OpenOptions::new()
        .access_mode(0x100) // FILE_WRITE_ATTRIBUTES
        .share_mode(7)
        .custom_flags(0x0220_0000) // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
        .open(link)?;
    let target = std::fs::canonicalize(target)?;
    let target = target.to_string_lossy();
    let target = target.strip_prefix(r"\\?\").unwrap_or(&target);
    let substitute: Vec<u16> = format!(r"\??\{target}").encode_utf16().collect();
    let print: Vec<u16> = target.encode_utf16().collect();
    let substitute_bytes = u16::try_from(substitute.len() * 2).unwrap();
    let print_bytes = u16::try_from(print.len() * 2).unwrap();
    let data_length = 8usize + substitute_bytes as usize + 2 + print_bytes as usize + 2;
    let mut buffer = vec![0u8; 8 + data_length];
    buffer[0..4].copy_from_slice(&0xa000_0003u32.to_le_bytes()); // MOUNT_POINT
    buffer[4..6].copy_from_slice(&(data_length as u16).to_le_bytes());
    buffer[8..10].copy_from_slice(&0u16.to_le_bytes());
    buffer[10..12].copy_from_slice(&substitute_bytes.to_le_bytes());
    buffer[12..14].copy_from_slice(&(substitute_bytes + 2).to_le_bytes());
    buffer[14..16].copy_from_slice(&print_bytes.to_le_bytes());
    for (index, value) in substitute.iter().enumerate() {
        let start = 16 + index * 2;
        buffer[start..start + 2].copy_from_slice(&value.to_le_bytes());
    }
    let print_start = 16 + substitute_bytes as usize + 2;
    for (index, value) in print.iter().enumerate() {
        let start = print_start + index * 2;
        buffer[start..start + 2].copy_from_slice(&value.to_le_bytes());
    }
    let mut returned = 0u32;
    let ok = unsafe {
        DeviceIoControl(
            handle.as_raw_handle() as _,
            0x0009_00a4, // FSCTL_SET_REPARSE_POINT
            buffer.as_ptr() as _,
            u32::try_from(buffer.len()).unwrap(),
            std::ptr::null_mut(),
            0,
            &mut returned,
            std::ptr::null_mut(),
        )
    };
    if ok == 0 {
        return Err(std::io::Error::last_os_error());
    }
    Ok(())
}

fn fixture() -> (tempfile::TempDir, PathBuf, PathBuf) {
    let dir = tempfile::tempdir().unwrap();
    let database = dir.path().join("repo").join("project-tree.sqlite");
    with_database_at(&database, |c| {
        c.execute_batch("INSERT INTO projects(id,name) VALUES('p','Project'); INSERT INTO themes(id,project_id,name) VALUES('t','p','Theme'); INSERT INTO assays(id,theme_id,name) VALUES('a','t','Assay'); INSERT INTO experiment_groups(id,assay_id,name) VALUES('g','a','Group'); INSERT INTO replicates(id,group_id,label) VALUES('r1','g','Sample 1'),('r2','g','Sample 2');")?;
        Ok(())
    }).unwrap();
    let source = dir.path().join("sources");
    std::fs::create_dir(&source).unwrap();
    (dir, database, source)
}
fn req(value: serde_json::Value) -> ImportRequest {
    serde_json::from_value(value).unwrap()
}
fn prepare(
    repo: &Arc<Repository>,
    paths: &[PathBuf],
    target: serde_json::Value,
    token: &str,
) -> JobView {
    execute(
        repo,
        req(json!({"action":"prepare","paths":paths,"target":target,"requestId":token})),
        None,
    )
    .unwrap()
    .jobs
    .remove(0)
}
fn raw(sample: &str) -> serde_json::Value {
    json!({"assayId":"a","stage":"raw","replicateId":sample})
}
fn action(repo: &Arc<Repository>, action: &str, id: &str) -> JobView {
    execute(repo, req(json!({"action":action,"jobId":id})), None)
        .unwrap()
        .jobs
        .remove(0)
}
fn wait(repo: &Arc<Repository>, id: &str) -> JobView {
    let deadline = Instant::now() + Duration::from_secs(90);
    loop {
        let view = action(repo, "get", id);
        if !repo.is_active(id) && view.status != "running" {
            return view;
        }
        assert!(Instant::now() < deadline, "worker failed to finish");
        std::thread::sleep(Duration::from_millis(10));
    }
}
fn hash(path: &Path) -> String {
    let mut file = std::fs::File::open(path).unwrap();
    filesystem::digest(&mut file).unwrap().0
}
fn assert_registry(repo: &Arc<Repository>, expected: i64) {
    with_database_at(&repo.database, |c| {
        for table in ["data_assets", "asset_versions", "asset_links"] {
            assert_eq!(
                c.query_row(&format!("SELECT count(*) FROM {table}"), [], |r| r
                    .get::<_, i64>(0))?,
                expected
            );
        }
        assert_eq!(
            c.query_row("SELECT count(*) FROM replicates", [], |r| r
                .get::<_, i64>(0))?,
            2
        );
        Ok(())
    })
    .unwrap();
}

#[test]
fn s3_streams_one_hundred_files_and_64mb_without_changing_source_bytes_or_mtime() {
    let (_dir, database, source) = fixture();
    let mut paths = Vec::new();
    for i in 0..100 {
        let p = source.join(format!("file-{i:03}.bin"));
        std::fs::write(&p, format!("test bytes {i}")).unwrap();
        paths.push(p);
    }
    let large = source.join("large.bin");
    let mut file = std::fs::File::create(&large).unwrap();
    let block = vec![73u8; 1024 * 1024];
    for _ in 0..64 {
        file.write_all(&block).unwrap();
    }
    file.sync_all().unwrap();
    drop(file);
    paths.push(large);
    let before: Vec<_> = paths
        .iter()
        .map(|p| (hash(p), std::fs::metadata(p).unwrap().modified().unwrap()))
        .collect();
    let repo = Repository::acquire(&database).unwrap();
    let prepared = prepare(&repo, &paths, raw("r1"), "batch");
    assert_eq!(prepared.status, "prepared");
    assert_registry(&repo, 0);
    action(&repo, "start", &prepared.id);
    let completed = wait(&repo, &prepared.id);
    assert_eq!(completed.status, "completed");
    assert_eq!(completed.completed_count, 101);
    assert_eq!(completed.copied_bytes, completed.total_bytes);
    for (i, file) in completed.files.iter().enumerate() {
        assert_eq!(file.sha256.as_deref(), Some(before[i].0.as_str()));
        assert_eq!(
            hash(&repo.objects.join(file.sha256.as_ref().unwrap())),
            before[i].0
        );
        assert_eq!(hash(&paths[i]), before[i].0);
        assert_eq!(
            std::fs::metadata(&paths[i]).unwrap().modified().unwrap(),
            before[i].1
        );
    }
    assert_registry(&repo, 101);
    drop(repo);
    let reopened = Repository::acquire(&database).unwrap();
    assert_eq!(action(&reopened, "get", &prepared.id).status, "completed");
    assert_eq!(std::fs::read_dir(&source).unwrap().count(), 101);
}

#[test]
fn s3_identical_bytes_across_samples_share_blob_and_replay_preserves_versions() {
    let (_dir, database, source) = fixture();
    let p = source.join("same.bin");
    std::fs::write(&p, b"same bytes").unwrap();
    let repo = Repository::acquire(&database).unwrap();
    let a = prepare(&repo, &[p.clone()], raw("r1"), "a");
    action(&repo, "start", &a.id);
    let first = wait(&repo, &a.id);
    let b = prepare(&repo, &[p.clone()], raw("r2"), "b");
    action(&repo, "start", &b.id);
    let second = wait(&repo, &b.id);
    assert_eq!(first.status, "completed");
    assert_eq!(second.status, "completed");
    assert_eq!(first.files[0].sha256, second.files[0].sha256);
    assert_ne!(
        first.files[0].asset_version_id,
        second.files[0].asset_version_id
    );
    assert_eq!(prepare(&repo, &[p.clone()], raw("r1"), "a").id, a.id);
    action(&repo, "start", &a.id);
    action(&repo, "retry", &a.id);
    assert_registry(&repo, 2);
    with_database_at(&database, |c| {
        assert_eq!(
            c.query_row("SELECT count(*) FROM blobs", [], |r| r.get::<_, i64>(0))?,
            1
        );
        Ok(())
    })
    .unwrap();
    assert_eq!(
        execute(
            &repo,
            req(json!({"action":"prepare","paths":[p],"target":raw("r2"),"requestId":"a"})),
            None
        )
        .unwrap_err()
        .code,
        "REQUEST_CONFLICT"
    );
}

#[test]
fn s3_actual_crash_child() {
    let Ok(database) = std::env::var("DATA_M_S3_CHILD_DB") else {
        return;
    };
    let point = std::env::var("DATA_M_S3_CHILD_POINT").unwrap();
    let source = PathBuf::from(std::env::var("DATA_M_S3_CHILD_SOURCE").unwrap());
    let repo = Repository::acquire(Path::new(&database)).unwrap();
    repo.set_hook(Arc::new(move |phase| {
        if phase == point {
            std::process::exit(77);
        }
        Ok(())
    }));
    let prepared = prepare(&repo, &[source], raw("r1"), "crash");
    action(&repo, "start", &prepared.id);
    let _ = wait(&repo, &prepared.id);
    panic!("faultpoint did not terminate the child");
}

#[test]
fn s3_process_exit_at_staged_and_published_recovers_original_registry_ids() {
    for point in ["staged", "published"] {
        let (_dir, database, source) = fixture();
        let p = source.join("crash.bin");
        std::fs::write(&p, b"crash recovery bytes").unwrap();
        let before = hash(&p);
        let status = std::process::Command::new(std::env::current_exe().unwrap())
            .args([
                "--exact",
                "import_jobs::tests::s3_actual_crash_child",
                "--nocapture",
            ])
            .env("DATA_M_S3_CHILD_DB", &database)
            .env("DATA_M_S3_CHILD_POINT", point)
            .env("DATA_M_S3_CHILD_SOURCE", &p)
            .status()
            .unwrap();
        assert_eq!(
            status.code(),
            Some(77),
            "child must actually exit at its faultpoint"
        );
        let ids = with_database_at(&database, |c| {
            let ids = c.query_row(
                "SELECT asset_id,version_id,link_id FROM import_files",
                [],
                |r| {
                    Ok((
                        r.get::<_, String>(0)?,
                        r.get::<_, String>(1)?,
                        r.get::<_, String>(2)?,
                    ))
                },
            )?;
            Ok(ids)
        })
        .unwrap();
        let repo = Repository::acquire(&database).unwrap();
        let jobs = execute(&repo, req(json!({"action":"list"})), None)
            .unwrap()
            .jobs;
        assert_eq!(jobs[0].status, "completed");
        assert_eq!(
            jobs[0].files[0].asset_version_id.as_deref(),
            Some(ids.1.as_str())
        );
        assert_registry(&repo, 1);
        assert_eq!(hash(&p), before);
    }
}

#[test]
fn s3_cancellation_is_serialized_before_and_after_registration_and_retry_is_stable() {
    for (point, count, status) in [
        ("before_commit", 0, "cancelled"),
        ("after_commit", 1, "completed"),
    ] {
        let (_dir, database, source) = fixture();
        let p = source.join("cancel.bin");
        std::fs::write(&p, b"cancel boundary").unwrap();
        let repo = Repository::acquire(&database).unwrap();
        let job = prepare(&repo, &[p], raw("r1"), "cancel");
        assert!(job.files[0].asset_version_id.is_none());
        let weak = Arc::downgrade(&repo);
        let id = job.id.clone();
        repo.set_hook(Arc::new(move |phase| {
            if phase == point {
                if let Some(repo) = weak.upgrade() {
                    action(&repo, "cancel", &id);
                }
            }
            Ok(())
        }));
        action(&repo, "start", &job.id);
        let stopped = wait(&repo, &job.id);
        assert_eq!(stopped.status, status);
        assert_eq!(stopped.completed_count, count);
        assert_registry(&repo, count as i64);
        repo.set_hook(Arc::new(|_| Ok(())));
        action(&repo, "retry", &job.id);
        let done = wait(&repo, &job.id);
        assert_eq!(done.status, "completed");
        assert_registry(&repo, 1);
    }
}

#[test]
fn s3_disk_full_writer_failure_retains_phase_and_retries_without_partial_registry() {
    let (_dir, database, source) = fixture();
    let p = source.join("disk-full.bin");
    std::fs::write(&p, vec![29u8; 4 * 1024 * 1024]).unwrap();
    let before = hash(&p);
    let repo = Repository::acquire(&database).unwrap();
    let job = prepare(&repo, &[p.clone()], raw("r1"), "disk");
    let writes = Arc::new(std::sync::atomic::AtomicUsize::new(0));
    let counter = writes.clone();
    repo.set_hook(Arc::new(move |phase| {
        if phase == "write" && counter.fetch_add(1, std::sync::atomic::Ordering::SeqCst) >= 1 {
            return Err(failure("DISK_WRITE_FAILED"));
        }
        Ok(())
    }));
    action(&repo, "start", &job.id);
    let failed = wait(&repo, &job.id);
    assert_eq!(failed.status, "failed");
    assert_eq!(
        failed.files[0].error_code.as_deref(),
        Some("DISK_WRITE_FAILED")
    );
    assert!(failed.files[0].asset_version_id.is_none());
    assert_registry(&repo, 0);
    with_database_at(&database, |c| {
        assert_eq!(
            c.query_row("SELECT phase FROM import_files", [], |r| r
                .get::<_, String>(0))?,
            "copying"
        );
        Ok(())
    })
    .unwrap();
    repo.set_hook(Arc::new(|_| Ok(())));
    action(&repo, "retry", &job.id);
    assert_eq!(wait(&repo, &job.id).status, "completed");
    assert_registry(&repo, 1);
    assert_eq!(hash(&p), before);
}

#[test]
fn s3_source_change_before_confirmation_is_rejected_and_live_source_handle_blocks_writes() {
    let (_dir, database, source) = fixture();
    let p = source.join("source.bin");
    std::fs::write(&p, b"original").unwrap();
    let repo = Repository::acquire(&database).unwrap();
    let job = prepare(&repo, &[p.clone()], raw("r1"), "changed");
    std::fs::write(&p, b"changed length").unwrap();
    action(&repo, "start", &job.id);
    let failed = wait(&repo, &job.id);
    assert_eq!(
        failed.files[0].error_code.as_deref(),
        Some("SOURCE_CHANGED")
    );
    assert_registry(&repo, 0);
    let p2 = source.join("live.bin");
    std::fs::write(&p2, vec![5u8; 2 * 1024 * 1024]).unwrap();
    let before = hash(&p2);
    let modified = std::fs::metadata(&p2).unwrap().modified().unwrap();
    let clone = p2.clone();
    let checked = Arc::new(std::sync::atomic::AtomicBool::new(false));
    let flag = checked.clone();
    repo.set_hook(Arc::new(move |phase| {
        if phase == "copying" {
            assert!(std::fs::OpenOptions::new()
                .write(true)
                .open(&clone)
                .is_err());
            assert!(std::fs::rename(&clone, clone.with_extension("renamed")).is_err());
            flag.store(true, std::sync::atomic::Ordering::SeqCst);
        }
        Ok(())
    }));
    let job = prepare(&repo, &[p2.clone()], raw("r1"), "live");
    action(&repo, "start", &job.id);
    assert_eq!(wait(&repo, &job.id).status, "completed");
    assert!(checked.load(std::sync::atomic::Ordering::SeqCst));
    assert_eq!(hash(&p2), before);
    assert_eq!(
        std::fs::metadata(&p2).unwrap().modified().unwrap(),
        modified
    );
    assert!(std::fs::OpenOptions::new().write(true).open(&p2).is_ok());
}

#[test]
fn s3_held_directory_and_verified_object_handles_block_namespace_and_byte_replacement() {
    let dir = tempfile::tempdir().unwrap();
    let parent = dir.path().join("held");
    std::fs::create_dir(&parent).unwrap();
    let moved = dir.path().join("moved");
    let guard = filesystem::Directories::hold(&parent, false).unwrap();
    assert!(std::fs::rename(&parent, &moved).is_err());
    drop(guard);
    std::fs::rename(&parent, &moved).unwrap();
    let (_fixture, database, source) = fixture();
    let p = source.join("object.bin");
    std::fs::write(&p, b"protected object").unwrap();
    let digest = hash(&p);
    let repo = Repository::acquire(&database).unwrap();
    let object = repo.objects.join(&digest);
    std::fs::copy(&p, &object).unwrap();
    let pinned = filesystem::object(&repo, &digest, 16).unwrap();
    assert!(std::fs::write(&object, b"bad").is_err());
    assert!(std::fs::remove_file(&object).is_err());
    assert!(std::fs::rename(&object, repo.objects.join("replaced")).is_err());
    drop(pinned);
    std::fs::write(&object, b"bad").unwrap();
}

#[test]
fn s3_bad_existing_blob_and_committed_blob_corruption_never_report_success() {
    let (_dir, database, source) = fixture();
    let p = source.join("blob.bin");
    std::fs::write(&p, b"valid blob").unwrap();
    let digest = hash(&p);
    let repo = Repository::acquire(&database).unwrap();
    let object = repo.objects.join(&digest);
    std::fs::write(&object, b"corrupt").unwrap();
    let job = prepare(&repo, &[p.clone()], raw("r1"), "bad-blob");
    action(&repo, "start", &job.id);
    let failed = wait(&repo, &job.id);
    assert_eq!(
        failed.files[0].error_code.as_deref(),
        Some("BLOB_INTEGRITY_FAILED")
    );
    assert_registry(&repo, 0);
    assert_eq!(std::fs::read(&object).unwrap(), b"corrupt");
    // The test explicitly restores disposable bytes; the service never repairs them.
    std::fs::write(&object, b"valid blob").unwrap();
    action(&repo, "retry", &job.id);
    assert_eq!(wait(&repo, &job.id).status, "completed");
    assert_registry(&repo, 1);
    std::fs::write(&object, b"external corruption").unwrap();
    drop(repo);
    let reopened = Repository::acquire(&database).unwrap();
    let view = action(&reopened, "get", &job.id);
    assert_eq!(view.status, "integrity_failed");
    assert_eq!(view.error_code.as_deref(), Some("BLOB_INTEGRITY_FAILED"));
    assert_eq!(
        action(&reopened, "retry", &job.id).status,
        "integrity_failed"
    );
    assert_registry(&reopened, 1);
}

#[test]
fn s3_staging_identity_race_fails_without_registering_substituted_bytes() {
    let (_dir, database, source) = fixture();
    let p = source.join("race.bin");
    std::fs::write(&p, b"expected").unwrap();
    let before = hash(&p);
    let repo = Repository::acquire(&database).unwrap();
    let weak = Arc::downgrade(&repo);
    repo.set_hook(Arc::new(move |phase| {
        if phase == "stage_closed" {
            let repo = weak.upgrade().unwrap();
            let stage = with_database_at(&repo.database, |c| {
                Ok(c.query_row(
                    "SELECT staging_name FROM import_files WHERE phase='copying'",
                    [],
                    |r| r.get::<_, String>(0),
                )?)
            })
            .unwrap();
            let path = repo.staging.join(stage);
            std::fs::rename(&path, path.with_extension("displaced")).unwrap();
            std::fs::write(&path, b"substitute").unwrap();
        }
        Ok(())
    }));
    let job = prepare(&repo, &[p.clone()], raw("r1"), "race");
    action(&repo, "start", &job.id);
    assert_eq!(wait(&repo, &job.id).status, "failed");
    assert_registry(&repo, 0);
    assert_eq!(hash(&p), before);
}

#[test]
fn s3_same_target_name_conflicts_but_assay_null_scope_is_also_unique() {
    let (_dir, database, source) = fixture();
    let p = source.join("name.bin");
    std::fs::write(&p, b"name bytes").unwrap();
    let repo = Repository::acquire(&database).unwrap();
    for (stage, expected) in [("raw", 1), ("statistics", 2)] {
        let target = if stage == "raw" {
            raw("r1")
        } else {
            json!({"assayId":"a","stage":stage})
        };
        let first = prepare(
            &repo,
            &[p.clone()],
            target.clone(),
            &format!("first-{stage}"),
        );
        action(&repo, "start", &first.id);
        assert_eq!(wait(&repo, &first.id).status, "completed");
        let second = prepare(&repo, &[p.clone()], target, &format!("second-{stage}"));
        action(&repo, "start", &second.id);
        let failed = wait(&repo, &second.id);
        assert_eq!(failed.files[0].error_code.as_deref(), Some("NAME_CONFLICT"));
        assert_registry(&repo, expected);
    }
}

fn junction(link: &Path, target: &Path) {
    use std::os::windows::process::CommandExt;
    let status = std::process::Command::new("cmd.exe")
        .args(["/C", "mklink", "/J"])
        .arg(link)
        .arg(target)
        .creation_flags(0x08000000)
        .status()
        .unwrap();
    assert!(
        status.success(),
        "disposable directory junction creation must succeed"
    );
}

#[test]
fn s3_reparse_sources_and_repository_subdirectories_are_rejected() {
    let (_dir, database, source) = fixture();
    std::fs::write(source.join("file.bin"), b"safe").unwrap();
    let repo = Repository::acquire(&database).unwrap();
    let link = source.parent().unwrap().join("source-junction");
    junction(&link, &source);
    let result = execute(
        &repo,
        req(
            json!({"action":"prepare","paths":[link.join("file.bin")],"target":raw("r1"),"requestId":"reparse"}),
        ),
        None,
    );
    assert_eq!(result.unwrap_err().code, "UNSAFE_PATH");
    let root = repo.root.clone();
    drop(repo);
    let cas = root.join("cas");
    std::fs::rename(&cas, root.join("old-cas")).unwrap();
    let outside = source.join("outside");
    std::fs::create_dir(&outside).unwrap();
    junction(&cas, &outside);
    assert_eq!(
        Repository::acquire(&database).err().unwrap().code,
        "UNSAFE_PATH"
    );
    assert_eq!(std::fs::read_dir(&outside).unwrap().count(), 0);
}

#[test]
fn s3_lock_child() {
    let Ok(database) = std::env::var("DATA_M_S3_LOCK_DB") else {
        return;
    };
    match Repository::acquire(Path::new(&database)) {
        Err(error) if error.code == "REPOSITORY_BUSY" => std::process::exit(78),
        Ok(_) => std::process::exit(79),
        Err(_) => std::process::exit(80),
    }
}

#[test]
fn s3_repository_os_lock_is_exclusive_and_released_without_deleting_lockfile() {
    let (_dir, database, _source) = fixture();
    let repo = Repository::acquire(&database).unwrap();
    let child = || {
        std::process::Command::new(std::env::current_exe().unwrap())
            .args([
                "--exact",
                "import_jobs::tests::s3_lock_child",
                "--nocapture",
            ])
            .env("DATA_M_S3_LOCK_DB", &database)
            .status()
            .unwrap()
            .code()
    };
    assert_eq!(child(), Some(78));
    drop(repo);
    assert_eq!(child(), Some(79));
    assert!(database.parent().unwrap().join("import.lock").is_file());
}

#[test]
fn s3_directory_pin_prevents_in_place_junction_retargeting() {
    let fixture = tempfile::tempdir().unwrap();
    let outside = fixture.path().join("outside");
    let vulnerable = fixture.path().join("vulnerable");
    std::fs::create_dir(&outside).unwrap();
    std::fs::create_dir(&vulnerable).unwrap();

    let held = filesystem::Directories::hold(&vulnerable, false).unwrap();
    set_junction_in_place(&vulnerable, &outside)
        .expect("a held but empty directory demonstrates the in-place junction threat");
    assert!(filesystem::database_pin(&held, "probe.sqlite").is_err());
    assert_eq!(std::fs::read_dir(&outside).unwrap().count(), 0);
    drop(held);
    std::fs::remove_dir(&vulnerable).unwrap();

    let protected = fixture.path().join("protected");
    std::fs::create_dir(&protected).unwrap();
    let mut held = filesystem::Directories::hold(&protected, false).unwrap();
    held.pin().unwrap();
    let error = set_junction_in_place(&protected, &outside).unwrap_err();
    assert_eq!(error.raw_os_error(), Some(145)); // ERROR_DIR_NOT_EMPTY
    held.validate().unwrap();
    assert_eq!(std::fs::read_dir(&outside).unwrap().count(), 0);
}

#[test]
fn s3_v1_to_v2_migration_rolls_back_atomically_and_preserves_stable_ids() {
    let fixture = tempfile::tempdir().unwrap();
    let database = fixture.path().join("project-tree.sqlite");
    let mut connection = rusqlite::Connection::open(&database).unwrap();
    connection
        .execute_batch(crate::project_tree::MIGRATION)
        .unwrap();
    connection
        .execute_batch(crate::project_tree::REQUEST_SCHEMA)
        .unwrap();
    connection
        .pragma_update(None, "application_id", 0x444d5331_i64)
        .unwrap();
    connection
        .pragma_update(None, "user_version", 1_i64)
        .unwrap();
    connection
        .execute(
            "INSERT INTO projects(id,name) VALUES('stable-project','Original')",
            [],
        )
        .unwrap();
    {
        let transaction = connection.transaction().unwrap();
        transaction
            .execute_batch(include_str!("../../migrations/0002_import_jobs.sql"))
            .unwrap();
        transaction
            .pragma_update(None, "user_version", 2_i64)
            .unwrap();
        // Deliberately drop without commit to simulate an interrupted upgrade.
    }
    drop(connection);

    crate::project_tree::validate_readonly(&database).unwrap();
    for _ in 0..3 {
        with_database_at(&database, |connection| {
            let version: i64 =
                connection.pragma_query_value(None, "user_version", |row| row.get(0))?;
            // S8 advances a valid v1 repository through v2 to v3 without losing IDs.
            assert_eq!(version, 3);
            let name: String = connection.query_row(
                "SELECT name FROM projects WHERE id='stable-project'",
                [],
                |row| row.get(0),
            )?;
            assert_eq!(name, "Original");
            Ok(())
        })
        .unwrap();
    }
}

#[test]
fn s3_tauri_ipc_imports_through_the_registered_async_command() {
    use tauri::test::{get_ipc_response, mock_builder, mock_context, noop_assets, MockRuntime};

    fn call(
        view: &tauri::WebviewWindow<MockRuntime>,
        request: serde_json::Value,
    ) -> Result<serde_json::Value, serde_json::Value> {
        get_ipc_response(
            view,
            tauri::webview::InvokeRequest {
                cmd: "import_jobs".into(),
                callback: tauri::ipc::CallbackFn(0),
                error: tauri::ipc::CallbackFn(1),
                url: "http://tauri.localhost".parse().unwrap(),
                body: tauri::ipc::InvokeBody::Json(json!({"request": request})),
                headers: Default::default(),
                invoke_key: tauri::test::INVOKE_KEY.to_string(),
            },
        )
        .map(|body| body.deserialize::<serde_json::Value>().unwrap())
    }

    let (_fixture, database, source) = fixture();
    let path = source.join("ipc.bin");
    std::fs::write(&path, b"native IPC bytes").unwrap();
    let app = mock_builder()
        .manage(crate::project_tree::TestDatabasePath(database))
        .invoke_handler(tauri::generate_handler![crate::import_jobs::import_jobs])
        .build(mock_context(noop_assets()))
        .unwrap();
    let view = tauri::WebviewWindowBuilder::new(&app, "main", Default::default())
        .build()
        .unwrap();
    let prepared = call(
        &view,
        json!({"action":"prepare","paths":[path],"target":raw("r1"),"requestId":"native-ipc"}),
    )
    .unwrap();
    let job_id = prepared["jobs"][0]["id"].as_str().unwrap().to_string();
    assert_eq!(prepared["jobs"][0]["status"], "prepared");
    call(&view, json!({"action":"start","jobId":job_id})).unwrap();
    let deadline = Instant::now() + Duration::from_secs(90);
    let completed = loop {
        let response = call(&view, json!({"action":"get","jobId":job_id})).unwrap();
        if response["jobs"][0]["status"] == "completed" {
            break response;
        }
        assert!(
            Instant::now() < deadline,
            "native IPC worker failed to finish"
        );
        std::thread::sleep(Duration::from_millis(10));
    };
    assert_eq!(completed["jobs"][0]["completedCount"], 1);
    assert!(completed["jobs"][0]["files"][0]["assetVersionId"].is_string());
    assert!(!completed.to_string().contains("sourcePath"));
}

#[test]
fn s11_inflight_import_blocks_backup_then_restores_frozen_and_pinned_records() {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::mpsc;

    let (directory, database, source) = fixture();
    let mut paths = Vec::new();
    for index in 0..8 {
        let file = source.join(format!("study-{index:02}.bin"));
        std::fs::write(&file, format!("S11 original test sample {index}")).unwrap();
        paths.push(file);
    }
    let source_before: Vec<_> = paths
        .iter()
        .map(|path| {
            (
                hash(path),
                std::fs::metadata(path).unwrap().modified().unwrap(),
            )
        })
        .collect();
    let repo = Repository::acquire(&database).unwrap();
    let prepared = prepare(&repo, &paths, raw("r1"), "s11-inflight-import");
    let (paused_tx, paused_rx) = mpsc::channel();
    let (release_tx, release_rx) = mpsc::channel();
    let release_rx = Arc::new(Mutex::new(release_rx));
    let once = AtomicBool::new(false);
    repo.set_hook(Arc::new(move |phase| {
        if phase == "staged" && !once.swap(true, Ordering::SeqCst) {
            paused_tx
                .send(())
                .map_err(|_| failure("DISK_WRITE_FAILED"))?;
            release_rx
                .lock()
                .map_err(|_| failure("DATABASE_UNAVAILABLE"))?
                .recv_timeout(Duration::from_secs(30))
                .map_err(|_| failure("DISK_WRITE_FAILED"))?;
        }
        Ok(())
    }));
    action(&repo, "start", &prepared.id);
    paused_rx
        .recv_timeout(Duration::from_secs(30))
        .expect("native worker must enter the staged checkpoint");
    let backups = directory.path().join("s11-backups");
    let restores = directory.path().join("s11-restores");
    let safety = |payload| -> crate::repository_safety::SafetyRequest {
        serde_json::from_value(payload).unwrap()
    };
    let refused = crate::repository_safety::execute_at(
        &database,
        &backups,
        &restores,
        safety(json!({"action":"backup"})),
        &|_| {},
    )
    .err()
    .expect("S7 must refuse an in-flight S3 import");
    assert_eq!(refused.code, "BACKUP_BUSY");
    assert!(
        !backups.exists(),
        "failed snapshot must not publish anything"
    );
    release_tx.send(()).unwrap();
    let finished = wait(&repo, &prepared.id);
    assert_eq!(finished.status, "completed");
    assert_eq!(finished.completed_count, paths.len());
    assert_registry(&repo, paths.len() as i64);
    repo.set_hook(Arc::new(|_| Ok(())));

    let pinned = finished.files[0].asset_version_id.as_deref().unwrap();
    with_database_at(&database, |connection| {
        connection.execute_batch(
            "INSERT INTO papers(id,project_id,title) VALUES('s11paper','p','Draft');
             INSERT INTO figures(id,paper_id,label) VALUES('s11figure','s11paper','F1');
             INSERT INTO panels(id,figure_id,label) VALUES('s11panel','s11figure','A');
             INSERT INTO statistical_analyses(id,assay_id,name)
             VALUES('s11analysis','a','Frozen');
             INSERT INTO analysis_samples(analysis_id,replicate_id,included)
             VALUES('s11analysis','r1',1);",
        )?;
        connection.execute(
            "INSERT INTO panel_asset_versions(panel_id,asset_version_id,usage)
             VALUES('s11panel',?1,'display')",
            [pinned],
        )?;
        connection.execute(
            "INSERT INTO analysis_asset_versions(analysis_id,asset_version_id,role)
             VALUES('s11analysis',?1,'input')",
            [pinned],
        )?;
        connection.execute(
            "UPDATE statistical_analyses
             SET frozen_at='2026-11-17T00:00:00Z'
             WHERE id='s11analysis'",
            [],
        )?;
        Ok(())
    })
    .unwrap();

    let snapshot = crate::repository_safety::execute_at(
        &database,
        &backups,
        &restores,
        safety(json!({"action":"backup"})),
        &|_| {},
    )
    .unwrap();
    let snapshot_json = serde_json::to_value(snapshot).unwrap();
    let id = snapshot_json["snapshotId"].as_str().unwrap();
    assert_eq!(snapshot_json["status"], "verified");
    let verified = crate::repository_safety::execute_at(
        &database,
        &backups,
        &restores,
        safety(json!({"action":"verify","snapshotId":id})),
        &|_| {},
    )
    .unwrap();
    assert_eq!(
        serde_json::to_value(verified).unwrap()["status"],
        "verified"
    );
    let restored = crate::repository_safety::execute_at(
        &database,
        &backups,
        &restores,
        safety(json!({
            "action":"restore",
            "snapshotId":id,
            "restoreLabel":"s11-isolated",
            "confirm":true
        })),
        &|_| {},
    )
    .unwrap();
    assert_eq!(
        serde_json::to_value(restored).unwrap()["status"],
        "restored"
    );
    let restored_db = restores.join("s11-isolated").join("project-tree.sqlite");
    with_database_at(&restored_db, |connection| {
        for (table, expected) in [
            ("data_assets", 8),
            ("blobs", 8),
            ("asset_versions", 8),
            ("asset_links", 8),
            ("panel_asset_versions", 1),
            ("analysis_asset_versions", 1),
        ] {
            let found: i64 =
                connection.query_row(&format!("SELECT count(*) FROM {table}"), [], |row| {
                    row.get(0)
                })?;
            assert_eq!(found, expected, "{table}");
        }
        let expected_version: String = connection.query_row(
            "SELECT asset_version_id FROM panel_asset_versions",
            [],
            |row| row.get(0),
        )?;
        assert_eq!(expected_version, pinned);
        let frozen: i64 = connection.query_row(
            "SELECT count(*) FROM statistical_analyses WHERE frozen_at IS NOT NULL",
            [],
            |row| row.get(0),
        )?;
        assert_eq!(frozen, 1);
        Ok(())
    })
    .unwrap();
    for (index, file) in paths.iter().enumerate() {
        let blob = finished.files[index].sha256.as_ref().unwrap();
        let object = restores
            .join("s11-isolated")
            .join("cas")
            .join("objects")
            .join(blob);
        assert_eq!(hash(&object), source_before[index].0);
        assert_eq!(hash(file), source_before[index].0);
        assert_eq!(
            std::fs::metadata(file).unwrap().modified().unwrap(),
            source_before[index].1
        );
    }
}
