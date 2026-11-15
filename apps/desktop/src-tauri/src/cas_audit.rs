//! Read-only SQLite→CAS integrity audit.
//!
//! Each registered SHA-256 is resolved to the fixed CAS fanout layout, never
//! to a caller-controlled path from the database. The verifier does not repair,
//! delete, rename, import, or create any file. No Tauri IPC is exposed.
use crate::cas;
use rusqlite::Connection;
use std::error::Error;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum BlobIssueKind {
    InvalidDigest,
    Missing,
    UnsafePath,
    WrongSize,
    HashMismatch,
    Unreadable,
}

#[derive(Debug)]
pub struct BlobIssue {
    pub sha256: String,
    pub kind: BlobIssueKind,
}

#[derive(Debug, Default)]
pub struct AuditReport {
    pub checked_blobs: usize,
    pub verified_blobs: usize,
    pub blob_issues: Vec<BlobIssue>,
    /// Non-sensitive database diagnostics; no paths or user identifiers.
    pub database_issues: Vec<&'static str>,
}

impl AuditReport {
    pub fn is_clean(&self) -> bool {
        self.blob_issues.is_empty() && self.database_issues.is_empty()
    }
}

fn invalid_input(message: &'static str) -> io::Error {
    io::Error::new(io::ErrorKind::InvalidInput, message)
}

fn validate_root(root: &Path) -> io::Result<()> {
    if !root.is_absolute() {
        return Err(invalid_input("CAS root must be absolute"));
    }
    let metadata = fs::symlink_metadata(root)?;
    if !metadata.is_dir() || metadata.file_type().is_symlink() {
        return Err(invalid_input("CAS root must be a real directory"));
    }
    Ok(())
}

fn valid_sha256(digest: &str) -> bool {
    digest.len() == 64
        && digest
            .as_bytes()
            .iter()
            .all(|character| character.is_ascii_hexdigit() && !character.is_ascii_uppercase())
}

fn object_components(root: &Path, digest: &str) -> [PathBuf; 4] {
    let objects = root.join("objects");
    let first = objects.join(&digest[..2]);
    let second = first.join(&digest[2..4]);
    let object = second.join(digest);
    [objects, first, second, object]
}

fn inspect_blob(root: &Path, digest: &str, expected_size: u64) -> Option<BlobIssueKind> {
    if !valid_sha256(digest) {
        return Some(BlobIssueKind::InvalidDigest);
    }
    let components = object_components(root, digest);
    for directory in &components[..3] {
        match fs::symlink_metadata(directory) {
            Err(error) if error.kind() == io::ErrorKind::NotFound => {
                return Some(BlobIssueKind::Missing);
            }
            Err(_) => return Some(BlobIssueKind::Unreadable),
            Ok(metadata) if metadata.file_type().is_symlink() || !metadata.is_dir() => {
                return Some(BlobIssueKind::UnsafePath);
            }
            Ok(_) => {}
        }
    }
    let object = &components[3];
    match fs::symlink_metadata(object) {
        Err(error) if error.kind() == io::ErrorKind::NotFound => {
            return Some(BlobIssueKind::Missing);
        }
        Err(_) => return Some(BlobIssueKind::Unreadable),
        Ok(metadata) if metadata.file_type().is_symlink() || !metadata.is_file() => {
            return Some(BlobIssueKind::UnsafePath);
        }
        Ok(metadata) if metadata.len() != expected_size => {
            return Some(BlobIssueKind::WrongSize);
        }
        Ok(_) => {}
    }
    match cas::hash_file(object) {
        Ok((actual, size)) if size == expected_size && actual == digest => None,
        Ok((_, size)) if size != expected_size => Some(BlobIssueKind::WrongSize),
        Ok(_) => Some(BlobIssueKind::HashMismatch),
        Err(_) => Some(BlobIssueKind::Unreadable),
    }
}

/// Verify all registered Blob rows and SQLite integrity without mutating either.
///
/// Reports are diagnostic only. A clean result cannot guarantee crash durability,
/// detect unregistered orphan files, or prevent concurrent path replacement.
/// Production use requires a repository lock and the backup/reconcile protocol.
pub fn audit_registered_blobs(
    database: &Connection,
    root: &Path,
) -> Result<AuditReport, Box<dyn Error>> {
    validate_root(root)?;
    let mut report = AuditReport::default();
    {
        let mut statement = database.prepare("PRAGMA integrity_check(10)")?;
        let rows = statement.query_map([], |row| row.get::<_, String>(0))?;
        for row in rows {
            if row? != "ok" {
                report.database_issues.push("sqlite_integrity_check");
            }
        }
    }
    {
        let mut statement = database.prepare("PRAGMA foreign_key_check")?;
        let mut rows = statement.query([])?;
        while rows.next()?.is_some() {
            report.database_issues.push("sqlite_foreign_key_violation");
        }
    }
    let mut statement = database.prepare("SELECT sha256,size_bytes FROM blobs ORDER BY sha256")?;
    let records = statement.query_map([], |row| {
        Ok((row.get::<_, String>(0)?, row.get::<_, i64>(1)?))
    })?;
    for record in records {
        let (digest, size_bytes) = record?;
        report.checked_blobs += 1;
        let issue = if size_bytes < 0 {
            Some(BlobIssueKind::WrongSize)
        } else {
            inspect_blob(root, &digest, size_bytes as u64)
        };
        match issue {
            Some(kind) => report.blob_issues.push(BlobIssue { sha256: digest, kind }),
            None => report.verified_blobs += 1,
        }
    }
    Ok(report)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::domain_db::initialize_empty_database;

    fn fixture() -> (tempfile::TempDir, Connection, String, PathBuf) {
        let workspace = tempfile::tempdir().unwrap();
        let source = workspace.path().join("original_肝脏.tif");
        fs::write(&source, b"scientific source bytes").unwrap();
        let repository = workspace.path().join("repository");
        let blob = cas::copy_file_to_cas(&source, &repository).unwrap();
        let mut database = Connection::open_in_memory().unwrap();
        initialize_empty_database(&mut database).unwrap();
        database
            .execute(
                "INSERT INTO blobs(sha256,size_bytes) VALUES(?1,?2)",
                rusqlite::params![blob.sha256, blob.size_bytes as i64],
            )
            .unwrap();
        (workspace, database, blob.sha256, blob.relative_path)
    }

    #[test]
    fn verifies_registered_blob_without_touching_source_or_database() {
        let (workspace, db, _, _) = fixture();
        let source = workspace.path().join("original_肝脏.tif");
        let before = fs::metadata(&source).unwrap();
        let root = workspace.path().join("repository");
        let report = audit_registered_blobs(&db, &root).unwrap();
        assert!(report.is_clean());
        assert_eq!(report.checked_blobs, 1);
        assert_eq!(report.verified_blobs, 1);
        assert_eq!(fs::read(&source).unwrap(), b"scientific source bytes");
        assert_eq!(fs::metadata(&source).unwrap().modified().ok(), before.modified().ok());
        let rows: i64 = db.query_row("SELECT count(*) FROM blobs", [], |row| row.get(0)).unwrap();
        assert_eq!(rows, 1);
    }

    #[test]
    fn reports_same_size_corruption_without_repairing_it() {
        let (workspace, db, _, object) = fixture();
        let path = workspace.path().join("repository").join(object);
        let tampered = b"scientific source bytES";
        assert_eq!(tampered.len(), fs::metadata(&path).unwrap().len() as usize);
        fs::write(&path, tampered).unwrap();
        let report = audit_registered_blobs(&db, &workspace.path().join("repository")).unwrap();
        assert_eq!(report.blob_issues[0].kind, BlobIssueKind::HashMismatch);
        assert_eq!(report.verified_blobs, 0);
        assert_eq!(fs::read(path).unwrap(), tampered);
    }

    #[test]
    fn distinguishes_missing_from_wrong_size() {
        let (workspace, db, _, object) = fixture();
        let root = workspace.path().join("repository");
        let path = root.join(&object);
        fs::write(&path, b"short").unwrap();
        let report = audit_registered_blobs(&db, &root).unwrap();
        assert_eq!(report.blob_issues[0].kind, BlobIssueKind::WrongSize);
        fs::remove_file(path).unwrap();
        let missing = audit_registered_blobs(&db, &root).unwrap();
        assert_eq!(missing.blob_issues[0].kind, BlobIssueKind::Missing);
    }

    #[test]
    fn detects_foreign_key_integrity_issues_without_modifying_rows() {
        let (workspace, db, _, _) = fixture();
        db.pragma_update(None, "foreign_keys", "OFF").unwrap();
        db.execute(
            "INSERT INTO themes(id,project_id,name) VALUES('broken','no-project','test')",
            [],
        )
        .unwrap();
        db.pragma_update(None, "foreign_keys", "ON").unwrap();
        let result = audit_registered_blobs(&db, &workspace.path().join("repository")).unwrap();
        assert!(result.database_issues.contains(&"sqlite_foreign_key_violation"));
        assert!(!result.is_clean());
        let exists: i64 = db
            .query_row("SELECT count(*) FROM themes WHERE id='broken'", [], |row| row.get(0))
            .unwrap();
        assert_eq!(exists, 1);
    }

    #[test]
    fn refuses_missing_or_relative_cas_root_without_creating_it() {
        let (workspace, db, _, _) = fixture();
        let missing = workspace.path().join("missing");
        assert!(audit_registered_blobs(&db, &missing).is_err());
        assert!(!missing.exists());
        assert!(audit_registered_blobs(&db, Path::new("relative")).is_err());
    }

    #[cfg(unix)]
    #[test]
    fn rejects_symlinked_digest_path_without_following_target() {
        let (workspace, db, _, object) = fixture();
        let root = workspace.path().join("repository");
        let path = root.join(object);
        fs::remove_file(&path).unwrap();
        std::os::unix::fs::symlink(workspace.path().join("original_肝脏.tif"), &path).unwrap();
        let report = audit_registered_blobs(&db, &root).unwrap();
        assert_eq!(report.blob_issues[0].kind, BlobIssueKind::UnsafePath);
    }
}
