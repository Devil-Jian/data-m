use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct ArchiveInfo {
    pub archive_id: String,
    pub schema_version: i64,
    pub data_dir: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct DataRecord {
    pub data_id: String,
    pub display_id: String,
    pub sha256: String,
    pub original_filename: String,
    pub file_extension: String,
    pub category: String,
    pub file_size: u64,
    pub created_at: String,
}
