#[cfg(test)]
mod tests {
    use super::ArchiveStore;
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
}
