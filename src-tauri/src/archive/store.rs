use std::{
    fs::{self, File, OpenOptions},
    io::{Read, Write},
    path::{Path, PathBuf},
    sync::Mutex,
    time::Duration,
};

use chrono::{SecondsFormat, Utc};
use rusqlite::{Connection, params};
use sha2::{Digest, Sha256};
use uuid::Uuid;

use super::{
    error::ArchiveError,
    models::{ArchiveInfo, DataRecord},
};

const COPY_BUFFER_SIZE: usize = 1024 * 1024;
const SCHEMA_VERSION: i64 = 1;
const INITIAL_MIGRATION: &str = include_str!("../../migrations/0001_initial.sql");

pub struct ArchiveStore {
    root: PathBuf,
    connection: Mutex<Connection>,
}

impl ArchiveStore {
    pub fn open(root: impl AsRef<Path>) -> Result<Self, ArchiveError> {
        let root = root.as_ref().to_path_buf();
        let database_dir = root.join("database");
        let blob_dir = root.join("blobs").join("sha256");
        let staging_dir = root.join("staging");

        fs::create_dir_all(&database_dir)?;
        fs::create_dir_all(&blob_dir)?;
        fs::create_dir_all(&staging_dir)?;

        let database_path = database_dir.join("archive.db");
        let mut connection = Connection::open(database_path)?;
        connection.busy_timeout(Duration::from_secs(5))?;
        connection.pragma_update(None, "journal_mode", "WAL")?;
        connection.pragma_update(None, "synchronous", "FULL")?;
        connection.pragma_update(None, "foreign_keys", true)?;

        let schema_version: i64 =
            connection.pragma_query_value(None, "user_version", |row| row.get(0))?;

        match schema_version {
            0 => Self::initialize_schema(&mut connection)?,
            SCHEMA_VERSION => {}
            unsupported => return Err(ArchiveError::UnsupportedSchema(unsupported)),
        }

        connection.query_row(
            "SELECT archive_id FROM archive_meta WHERE singleton = 1",
            [],
            |row| row.get::<_, String>(0),
        )?;

        Ok(Self {
            root,
            connection: Mutex::new(connection),
        })
    }

    pub fn import_file(&self, source: impl AsRef<Path>) -> Result<DataRecord, ArchiveError> {
        let source_path = source.as_ref();
        let metadata = fs::metadata(source_path).map_err(|error| {
            if error.kind() == std::io::ErrorKind::NotFound {
                ArchiveError::SourceNotFound(source_path.to_path_buf())
            } else {
                ArchiveError::Io(error)
            }
        })?;

        if !metadata.is_file() {
            return Err(ArchiveError::InvalidSource(source_path.to_path_buf()));
        }

        let original_filename = source_path
            .file_name()
            .and_then(|name| name.to_str())
            .map(str::to_owned)
            .ok_or(ArchiveError::InvalidFilename)?;
        let file_extension = source_path
            .extension()
            .and_then(|extension| extension.to_str())
            .unwrap_or_default()
            .to_ascii_lowercase();

        let mut source_file = File::open(source_path)?;
        let staging_path = self
            .root
            .join("staging")
            .join(format!("{}.part", Uuid::now_v7()));
        let _staging_guard = StagingGuard::new(staging_path.clone());
        let (sha256, file_size) = stage_and_hash(&mut source_file, &staging_path)?;
        let database_file_size =
            i64::try_from(file_size).map_err(|_| ArchiveError::FileTooLarge)?;

        let relative_path = format!("blobs/sha256/{}/{}", &sha256[..2], sha256);
        let destination = self
            .root
            .join("blobs")
            .join("sha256")
            .join(&sha256[..2])
            .join(&sha256);
        let parent = destination
            .parent()
            .expect("content-addressed Blob path always has a parent");
        fs::create_dir_all(parent)?;
        promote_staged_blob(&staging_path, &destination, &sha256, file_size)?;

        let data_id = Uuid::now_v7().to_string();
        let display_id = format!("RD-{}", data_id.replace('-', "").to_ascii_uppercase());
        let category = category_for_extension(&file_extension).to_owned();
        let created_at = utc_timestamp();

        let mut connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let transaction = connection.transaction()?;

        transaction.execute(
            "INSERT INTO blobs (sha256, file_size, relative_path, media_type, created_at)
             VALUES (?1, ?2, ?3, NULL, ?4)
             ON CONFLICT(sha256) DO NOTHING",
            params![sha256, database_file_size, relative_path, created_at],
        )?;

        let (stored_size, stored_path): (i64, String) = transaction.query_row(
            "SELECT file_size, relative_path FROM blobs WHERE sha256 = ?1",
            params![sha256],
            |row| Ok((row.get(0)?, row.get(1)?)),
        )?;

        if stored_size != database_file_size || stored_path != relative_path {
            return Err(ArchiveError::CorruptBlob(sha256));
        }

        transaction.execute(
            "INSERT INTO data_records (
                data_id, display_id, blob_sha256, original_filename,
                file_extension, category, created_at
             ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)",
            params![
                data_id,
                display_id,
                sha256,
                original_filename,
                file_extension,
                category,
                created_at
            ],
        )?;

        transaction.commit()?;

        Ok(DataRecord {
            data_id,
            display_id,
            sha256,
            original_filename,
            file_extension,
            category,
            file_size,
            created_at,
        })
    }

    pub fn list_data_records(&self) -> Result<Vec<DataRecord>, ArchiveError> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let mut statement = connection.prepare(
            "SELECT data_id, display_id, blob_sha256, original_filename,
                    file_extension, category, file_size, created_at
             FROM data_records
             WHERE deleted_at IS NULL
             ORDER BY created_at DESC, data_id DESC",
        )?;

        let rows = statement.query_map([], |row| {
            let file_size: i64 = row.get(6)?;
            Ok(DataRecord {
                data_id: row.get(0)?,
                display_id: row.get(1)?,
                sha256: row.get(2)?,
                original_filename: row.get(3)?,
                file_extension: row.get(4)?,
                category: row.get(5)?,
                file_size: file_size as u64,
                created_at: row.get(7)?,
            })
        })?;

        let mut records = Vec::new();
        for row in rows {
            records.push(row?);
        }
        Ok(records)
    }

    pub fn archive_info(&self) -> Result<ArchiveInfo, ArchiveError> {
        let connection = self
            .connection
            .lock()
            .map_err(|_| ArchiveError::LockPoisoned)?;
        let (archive_id, schema_version): (String, i64) = connection.query_row(
            "SELECT archive_id, schema_version
             FROM archive_meta
             WHERE singleton = 1",
            [],
            |row| Ok((row.get(0)?, row.get(1)?)),
        )?;

        Ok(ArchiveInfo {
            archive_id,
            schema_version,
            data_dir: self.root.to_string_lossy().into_owned(),
        })
    }

    fn initialize_schema(connection: &mut Connection) -> Result<(), ArchiveError> {
        let transaction = connection.transaction()?;
        transaction.execute_batch(INITIAL_MIGRATION)?;

        let archive_id = Uuid::now_v7().to_string();
        let created_at = utc_timestamp();
        transaction.execute(
            "INSERT INTO archive_meta (
                singleton, archive_id, schema_version, created_at, updated_at
             ) VALUES (1, ?1, ?2, ?3, ?3)",
            params![archive_id, SCHEMA_VERSION, created_at],
        )?;

        transaction.commit()?;
        Ok(())
    }
}

struct StagingGuard {
    path: PathBuf,
}

impl StagingGuard {
    fn new(path: PathBuf) -> Self {
        Self { path }
    }
}

impl Drop for StagingGuard {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.path);
    }
}

fn stage_and_hash(
    source: &mut File,
    staging_path: &Path,
) -> Result<(String, u64), ArchiveError> {
    let mut staging = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(staging_path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0_u8; COPY_BUFFER_SIZE];
    let mut file_size = 0_u64;

    loop {
        let bytes_read = source.read(&mut buffer)?;
        if bytes_read == 0 {
            break;
        }

        staging.write_all(&buffer[..bytes_read])?;
        hasher.update(&buffer[..bytes_read]);
        file_size = file_size
            .checked_add(bytes_read as u64)
            .ok_or(ArchiveError::FileTooLarge)?;
    }

    staging.flush()?;
    staging.sync_all()?;
    Ok((hex::encode(hasher.finalize()), file_size))
}

fn promote_staged_blob(
    staging_path: &Path,
    destination: &Path,
    expected_sha256: &str,
    expected_size: u64,
) -> Result<(), ArchiveError> {
    if destination.exists() {
        verify_blob(destination, expected_sha256, expected_size)?;
        fs::remove_file(staging_path)?;
        return Ok(());
    }

    match fs::rename(staging_path, destination) {
        Ok(()) => Ok(()),
        Err(error) if destination.exists() => {
            verify_blob(destination, expected_sha256, expected_size)?;
            fs::remove_file(staging_path)?;
            Ok(())
        }
        Err(error) => Err(ArchiveError::Io(error)),
    }
}

fn verify_blob(
    path: &Path,
    expected_sha256: &str,
    expected_size: u64,
) -> Result<(), ArchiveError> {
    let metadata = fs::metadata(path)
        .map_err(|_| ArchiveError::CorruptBlob(expected_sha256.to_owned()))?;
    if !metadata.is_file() || metadata.len() != expected_size {
        return Err(ArchiveError::CorruptBlob(expected_sha256.to_owned()));
    }

    let (actual_sha256, actual_size) = hash_file(path)?;
    if actual_size != expected_size || actual_sha256 != expected_sha256 {
        return Err(ArchiveError::CorruptBlob(expected_sha256.to_owned()));
    }
    Ok(())
}

fn hash_file(path: &Path) -> Result<(String, u64), ArchiveError> {
    let mut file = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0_u8; COPY_BUFFER_SIZE];
    let mut file_size = 0_u64;

    loop {
        let bytes_read = file.read(&mut buffer)?;
        if bytes_read == 0 {
            break;
        }
        hasher.update(&buffer[..bytes_read]);
        file_size = file_size
            .checked_add(bytes_read as u64)
            .ok_or(ArchiveError::FileTooLarge)?;
    }

    Ok((hex::encode(hasher.finalize()), file_size))
}

fn category_for_extension(extension: &str) -> &'static str {
    match extension {
        "png" | "jpg" | "jpeg" | "tif" | "tiff" | "bmp" | "gif" | "webp" | "czi" | "nd2"
        | "lif" | "ims" | "svs" => "image",
        "csv" | "tsv" | "xls" | "xlsx" | "ods" => "spreadsheet",
        "pdf" | "doc" | "docx" | "txt" | "rtf" | "md" => "document",
        _ => "other",
    }
}

fn utc_timestamp() -> String {
    Utc::now().to_rfc3339_opts(SecondsFormat::Millis, true)
}

#[cfg(test)]
mod tests {
    use super::ArchiveStore;
    use rusqlite::Connection;
    use std::{
        fs,
        path::{Path, PathBuf},
    };
    use tempfile::TempDir;

    fn open_test_store(temp: &TempDir) -> (PathBuf, ArchiveStore) {
        let root = temp.path().join("archive");
        let store = ArchiveStore::open(&root).expect("archive should initialize");
        (root, store)
    }

    fn blob_path(root: &Path, sha256: &str) -> PathBuf {
        root.join("blobs")
            .join("sha256")
            .join(&sha256[..2])
            .join(sha256)
    }

    #[test]
    fn opens_archive_and_keeps_archive_id_stable() {
        let temp = TempDir::new().expect("temporary directory");
        let (root, first) = open_test_store(&temp);
        let first_info = first.archive_info().expect("archive info");

        assert!(root.join("database").join("archive.db").is_file());
        assert!(root.join("blobs").join("sha256").is_dir());
        assert!(root.join("staging").is_dir());
        assert_eq!(first_info.schema_version, 1);

        drop(first);

        let second = ArchiveStore::open(&root).expect("archive should reopen");
        let second_info = second.archive_info().expect("archive info");
        assert_eq!(first_info.archive_id, second_info.archive_id);
    }

    #[test]
    fn import_file_preserves_source_bytes_and_sha256() {
        let temp = TempDir::new().expect("temporary directory");
        let (root, store) = open_test_store(&temp);
        let source = temp.path().join("hello.txt");
        fs::write(&source, b"hello world").expect("write source file");

        let record = store.import_file(&source).expect("file should import");

        assert_eq!(
            record.sha256,
            "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9"
        );
        assert_eq!(record.file_size, 11);
        assert_eq!(
            fs::read(blob_path(&root, &record.sha256)).unwrap(),
            b"hello world"
        );
    }

    #[test]
    fn duplicate_content_uses_one_blob_but_keeps_distinct_data_ids() {
        let temp = TempDir::new().expect("temporary directory");
        let (root, store) = open_test_store(&temp);
        let source = temp.path().join("repeat.txt");
        fs::write(&source, b"identical research data").expect("write source file");

        let first = store.import_file(&source).expect("first import");
        let second = store.import_file(&source).expect("second import");

        assert_ne!(first.data_id, second.data_id);
        assert_eq!(first.sha256, second.sha256);
        assert!(blob_path(&root, &first.sha256).is_file());

        let shard = blob_path(&root, &first.sha256)
            .parent()
            .expect("blob shard");
        let blob_count = fs::read_dir(shard)
            .expect("read blob shard")
            .filter_map(Result::ok)
            .filter(|entry| entry.path().is_file())
            .count();
        assert_eq!(blob_count, 1);
        assert_eq!(store.list_data_records().unwrap().len(), 2);
    }

    #[test]
    fn same_filename_with_different_content_does_not_overwrite() {
        let temp = TempDir::new().expect("temporary directory");
        let (root, store) = open_test_store(&temp);
        let first_dir = temp.path().join("first");
        let second_dir = temp.path().join("second");
        fs::create_dir_all(&first_dir).unwrap();
        fs::create_dir_all(&second_dir).unwrap();
        let first_source = first_dir.join("experiment.raw");
        let second_source = second_dir.join("experiment.raw");
        fs::write(&first_source, b"first acquisition").unwrap();
        fs::write(&second_source, b"second acquisition").unwrap();

        let first = store.import_file(&first_source).expect("first import");
        let second = store.import_file(&second_source).expect("second import");

        assert_ne!(first.sha256, second.sha256);
        assert_ne!(first.data_id, second.data_id);
        assert_eq!(
            fs::read(blob_path(&root, &first.sha256)).unwrap(),
            b"first acquisition"
        );
        assert_eq!(
            fs::read(blob_path(&root, &second.sha256)).unwrap(),
            b"second acquisition"
        );
    }

    #[test]
    fn missing_source_does_not_create_a_data_record() {
        let temp = TempDir::new().expect("temporary directory");
        let (_root, store) = open_test_store(&temp);
        let missing = temp.path().join("missing.raw");

        assert!(store.import_file(&missing).is_err());
        assert!(store.list_data_records().unwrap().is_empty());
    }

    #[test]
    fn list_data_records_returns_import_metadata() {
        let temp = TempDir::new().expect("temporary directory");
        let (_root, store) = open_test_store(&temp);
        let source = temp.path().join("dataset.txt");
        fs::write(&source, b"hello world").unwrap();

        let imported = store.import_file(&source).expect("file should import");
        let records = store.list_data_records().expect("list records");

        assert_eq!(records.len(), 1);
        assert_eq!(records[0].data_id, imported.data_id);
        assert_eq!(records[0].display_id, imported.display_id);
        assert!(records[0].display_id.starts_with("RD-"));
        assert_eq!(records[0].sha256, imported.sha256);
        assert_eq!(records[0].original_filename, "dataset.txt");
        assert_eq!(records[0].file_extension, "txt");
        assert_eq!(records[0].category, "document");
        assert_eq!(records[0].file_size, 11);
        assert!(!records[0].created_at.is_empty());
    }

    #[test]
    fn corrupted_existing_blob_is_rejected_instead_of_reused() {
        let temp = TempDir::new().expect("temporary directory");
        let (root, store) = open_test_store(&temp);
        let source = temp.path().join("integrity.txt");
        fs::write(&source, b"immutable content").unwrap();

        let first = store.import_file(&source).expect("first import");
        fs::write(blob_path(&root, &first.sha256), b"corrupted bytes")
            .expect("corrupt stored blob");

        assert!(store.import_file(&source).is_err());
        assert_eq!(store.list_data_records().unwrap().len(), 1);
    }

    #[test]
    fn database_insert_failure_does_not_create_a_data_record() {
        let temp = TempDir::new().expect("temporary directory");
        let (root, store) = open_test_store(&temp);
        let source = temp.path().join("triggered.txt");
        fs::write(&source, b"transactional data").unwrap();

        let connection = Connection::open(root.join("database").join("archive.db"))
            .expect("open archive database");
        connection
            .execute_batch(
                "CREATE TRIGGER reject_data_records
                 BEFORE INSERT ON data_records
                 BEGIN
                     SELECT RAISE(ABORT, 'forced test failure');
                 END;",
            )
            .expect("install test trigger");
        drop(connection);

        assert!(store.import_file(&source).is_err());
        assert!(store.list_data_records().unwrap().is_empty());
    }
}
