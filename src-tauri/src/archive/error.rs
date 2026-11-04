use std::path::PathBuf;

#[derive(Debug, thiserror::Error)]
pub enum ArchiveError {
    #[error("filesystem operation failed: {0}")]
    Io(#[from] std::io::Error),

    #[error("SQLite operation failed: {0}")]
    Database(#[from] rusqlite::Error),

    #[error("source file does not exist: {0}")]
    SourceNotFound(PathBuf),

    #[error("source path is not a regular file: {0}")]
    InvalidSource(PathBuf),

    #[error("source file name is not valid UTF-8")]
    InvalidFilename,

    #[error("source file disappeared or changed during import: {0}")]
    SourceChanged(PathBuf),

    #[error("archive schema version {0} is not supported")]
    UnsupportedSchema(i64),

    #[error("stored Blob failed SHA-256 integrity verification: {0}")]
    CorruptBlob(String),

    #[error("file is too large for the SQLite metadata representation")]
    FileTooLarge,

    #[error("archive database lock was poisoned")]
    LockPoisoned,
}
