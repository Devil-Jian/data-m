//! Prototype local CAS copy. Not wired to Tauri IPC or production repositories.
//! No operation is allowed to modify or remove the imported source file.
use sha2::{Digest, Sha256};
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use tempfile::Builder;

#[derive(Debug)]
pub struct StoredBlob {
    pub sha256: String,
    pub size_bytes: u64,
    pub relative_path: PathBuf,
    pub deduplicated: bool,
}

fn reject(message: &'static str) -> io::Error {
    io::Error::new(io::ErrorKind::InvalidInput, message)
}

fn safe_directory(path: &Path) -> io::Result<()> {
    fs::create_dir_all(path)?;
    let metadata = fs::symlink_metadata(path)?;
    if !metadata.file_type().is_dir() || metadata.file_type().is_symlink() {
        return Err(reject("CAS directory must be a real directory"));
    }
    Ok(())
}

fn hash_file(path: &Path) -> io::Result<(String, u64)> {
    let mut input = File::open(path)?;
    let mut hasher = Sha256::new();
    let mut size = 0_u64;
    let mut buffer = vec![0_u8; 1024 * 1024];
    loop {
        let length = input.read(&mut buffer)?;
        if length == 0 {
            break;
        }
        hasher.update(&buffer[..length]);
        size += length as u64;
    }
    Ok((format!("{:x}", hasher.finalize()), size))
}

/// A caller-provided root must be a dedicated empty or existing controlled CAS root.
/// Only call from a reviewed application service after repository and permission checks.
pub fn copy_file_to_cas(source: &Path, root: &Path) -> io::Result<StoredBlob> {
    if !source.is_absolute() || !root.is_absolute() {
        return Err(reject("source and repository paths must be absolute"));
    }
    let source_meta = fs::symlink_metadata(source)?;
    if !source_meta.is_file() || source_meta.file_type().is_symlink() {
        return Err(reject("source must be a regular file, not a symlink"));
    }
    safe_directory(root)?;
    let canonical_root = fs::canonicalize(root)?;
    let canonical_source = fs::canonicalize(source)?;
    if canonical_source.starts_with(&canonical_root) {
        return Err(reject("source is already inside the CAS repository"));
    }
    let staging = root.join("staging");
    let objects = root.join("objects");
    safe_directory(&staging)?;
    safe_directory(&objects)?;
    let mut temp = Builder::new().prefix("stage-").tempfile_in(&staging)?;
    let mut input = File::open(source)?;
    let mut sha = Sha256::new();
    let mut size = 0_u64;
    let mut buffer = vec![0_u8; 1024 * 1024];
    loop {
        let count = input.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        temp.write_all(&buffer[..count])?;
        sha.update(&buffer[..count]);
        size += count as u64;
    }
    temp.flush()?;
    temp.as_file().sync_all()?;

    // If the source changed during this pass, refuse to register mixed bytes.
    let after = fs::metadata(source)?;
    if size != source_meta.len()
        || after.len() != source_meta.len()
        || after.modified().ok() != source_meta.modified().ok()
    {
        return Err(reject("source changed during staging"));
    }
    let digest = format!("{:x}", sha.finalize());
    let fanout = PathBuf::from("objects").join(&digest[0..2]).join(&digest[2..4]);
    let parent = root.join(&fanout);
    safe_directory(&parent)?;
    let relative_path = fanout.join(&digest);
    let target = root.join(&relative_path);

    let deduplicated = match temp.persist_noclobber(&target) {
        Ok(_) => false,
        Err(problem) if problem.error.kind() == io::ErrorKind::AlreadyExists => {
            // Never trust an existing CAS filename without checking actual bytes.
            let current = fs::symlink_metadata(&target)?;
            if current.file_type().is_symlink() || !current.is_file() {
                return Err(reject("CAS target is not a regular file"));
            }
            let (existing_digest, existing_size) = hash_file(&target)?;
            if existing_digest != digest || existing_size != size {
                return Err(reject("CAS content collision or corruption"));
            }
            true
        }
        Err(problem) => return Err(problem.error),
    };
    Ok(StoredBlob {
        sha256: digest,
        size_bytes: size,
        relative_path,
        deduplicated,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn imports_without_modifying_source_and_deduplicates() {
        let workspace = tempfile::tempdir().unwrap();
        let repository = workspace.path().join("repository");
        let source = workspace.path().join("source_中文.tif");
        let bytes: Vec<u8> = (0..100_000).map(|value| (value % 251) as u8).collect();
        fs::write(&source, &bytes).unwrap();
        let before = fs::metadata(&source).unwrap();
        let first = copy_file_to_cas(&source, &repository).unwrap();
        let second = copy_file_to_cas(&source, &repository).unwrap();
        assert!(!first.deduplicated);
        assert!(second.deduplicated);
        assert_eq!(first.sha256, second.sha256);
        assert_eq!(first.size_bytes, bytes.len() as u64);
        assert_eq!(fs::read(&source).unwrap(), bytes);
        assert_eq!(fs::read(repository.join(&first.relative_path)).unwrap(), bytes);
        let after = fs::metadata(&source).unwrap();
        assert_eq!(before.len(), after.len());
        assert_eq!(before.modified().ok(), after.modified().ok());
        assert_eq!(fs::read_dir(repository.join("staging")).unwrap().count(), 0);
    }

    #[test]
    fn rejects_invalid_source_without_creating_new_version() {
        let workspace = tempfile::tempdir().unwrap();
        let repo = workspace.path().join("repo");
        let absent = workspace.path().join("absent.tif");
        assert!(copy_file_to_cas(&absent, &repo).is_err());
        assert!(copy_file_to_cas(Path::new("relative.tif"), &repo).is_err());
        let folder = workspace.path().join("folder");
        fs::create_dir(&folder).unwrap();
        assert!(copy_file_to_cas(&folder, &repo).is_err());
    }

    #[test]
    fn refuses_repository_inside_source_and_corrupt_existing_target() {
        let workspace = tempfile::tempdir().unwrap();
        let root = workspace.path().join("cas");
        let original = workspace.path().join("original.tif");
        fs::write(&original, b"science").unwrap();
        let blob = copy_file_to_cas(&original, &root).unwrap();
        let target = root.join(&blob.relative_path);
        assert!(copy_file_to_cas(&target, &root).is_err());
        // Simulate external corruption: never overwrite a hash-addressed object.
        fs::write(&target, b"corrupted").unwrap();
        assert!(copy_file_to_cas(&original, &root).is_err());
        assert_eq!(fs::read(&target).unwrap(), b"corrupted");
    }

    #[cfg(unix)]
    #[test]
    fn refuses_symlink_sources() {
        let workspace = tempfile::tempdir().unwrap();
        let target = workspace.path().join("real.tif");
        fs::write(&target, b"science").unwrap();
        let shortcut = workspace.path().join("shortcut.tif");
        std::os::unix::fs::symlink(&target, &shortcut).unwrap();
        assert!(copy_file_to_cas(&shortcut, &workspace.path().join("cas")).is_err());
    }
}
