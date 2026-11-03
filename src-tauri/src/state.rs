use std::{
    env,
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
};

use serde::Serialize;

use crate::archive::{ArchiveInfo, ArchiveStore};

#[derive(Debug, Clone, Serialize)]
pub struct ArchiveStatus {
    pub initialized: bool,
    pub data_dir: Option<String>,
    pub archive_id: Option<String>,
    pub schema_version: Option<i64>,
    pub error: Option<String>,
}

impl ArchiveStatus {
    fn uninitialized(path: &Path, error: Option<String>) -> Self {
        Self {
            initialized: false,
            data_dir: Some(path.to_string_lossy().into_owned()),
            archive_id: None,
            schema_version: None,
            error,
        }
    }

    fn initialized(info: ArchiveInfo) -> Self {
        Self {
            initialized: true,
            data_dir: Some(info.data_dir),
            archive_id: Some(info.archive_id),
            schema_version: Some(info.schema_version),
            error: None,
        }
    }
}

pub struct AppState {
    default_dir: PathBuf,
    store: Mutex<Option<Arc<ArchiveStore>>>,
    startup_error: Mutex<Option<String>>,
}

impl AppState {
    pub fn new() -> Self {
        let default_dir = default_archive_dir();
        let state = Self {
            default_dir: default_dir.clone(),
            store: Mutex::new(None),
            startup_error: Mutex::new(None),
        };

        if !default_location_available(&default_dir) {
            let message = "默认 D 盘不可用。请选择一个本地数据目录进行初始化。".to_owned();
            *state.startup_error.lock().expect("startup lock") = Some(message);
        } else if let Err(message) = state.initialize(Some(default_dir.to_string_lossy().into_owned())) {
            *state.startup_error.lock().expect("startup lock") = Some(message);
        }
        state
    }

    pub fn status(&self) -> Result<ArchiveStatus, String> {
        let store = self.store.lock().map_err(|_| "归档库状态锁异常")?;
        if let Some(store) = store.as_ref() {
            return store
                .archive_info()
                .map(ArchiveStatus::initialized)
                .map_err(|err| format!("无法读取归档库状态：{err}"));
        }
        let error = self
            .startup_error
            .lock()
            .map_err(|_| "归档库状态锁异常")?
            .clone();
        Ok(ArchiveStatus::uninitialized(&self.default_dir, error))
    }

    pub fn initialize(&self, path: Option<String>) -> Result<ArchiveStatus, String> {
        let root = match path {
            Some(value) if !value.trim().is_empty() => PathBuf::from(value),
            Some(_) => return Err("数据目录不能为空".to_owned()),
            None => self.default_dir.clone(),
        };

        validate_local_archive_dir(&root)?;
        let candidate = Arc::new(
            ArchiveStore::open(&root).map_err(|error| format!("无法初始化归档库：{error}"))?,
        );
        let info = candidate
            .archive_info()
            .map_err(|error| format!("无法读取归档库信息：{error}"))?;
        *self.store.lock().map_err(|_| "归档库状态锁异常")? = Some(candidate);
        *self.startup_error.lock().map_err(|_| "归档库状态锁异常")? = None;

        Ok(ArchiveStatus::initialized(info))
    }

    pub fn store(&self) -> Result<Arc<ArchiveStore>, String> {
        self.store
            .lock()
            .map_err(|_| "归档库状态锁异常".to_owned())?
            .clone()
            .ok_or_else(|| "归档库尚未初始化，请先选择一个本地数据目录".to_owned())
    }
}

fn default_archive_dir() -> PathBuf {
    if let Some(path) = env::var_os("PAPER_ARCHIVE_DATA_DIR") {
        return PathBuf::from(path);
    }

    #[cfg(windows)]
    {
        PathBuf::from(r"D:\PaperArchiveData")
    }

    #[cfg(not(windows))]
    {
        let home = env::var_os("HOME").unwrap_or_else(|| ".".into());
        PathBuf::from(home).join("PaperArchiveData-dev")
    }
}

fn default_location_available(path: &Path) -> bool {
    #[cfg(windows)]
    if env::var_os("PAPER_ARCHIVE_DATA_DIR").is_none() {
        return Path::new(r"D:\").exists();
    }

    path.is_absolute()
}

fn validate_local_archive_dir(path: &Path) -> Result<(), String> {
    if !path.is_absolute() {
        return Err("必须选择绝对路径，不能使用相对目录".to_owned());
    }
    if path.as_os_str().is_empty() {
        return Err("数据目录不能为空".to_owned());
    }

    #[cfg(windows)]
    {
        use std::path::{Component, Prefix};
        let disk = match path.components().next() {
            Some(Component::Prefix(prefix)) => match prefix.kind() {
                Prefix::Disk(letter) | Prefix::VerbatimDisk(letter) => letter,
                _ => return Err("不支持 UNC 网络共享路径，请选择本地磁盘目录".to_owned()),
            },
            _ => return Err("请选择本地磁盘的绝对路径".to_owned()),
        };
        let disk_root = format!("{}:\\", disk as char);
        let wide = std::ffi::OsStr::new(&disk_root)
            .encode_wide()
            .chain(std::iter::once(0))
            .collect::<Vec<u16>>();
        let drive_type = unsafe { get_drive_type_w(wide.as_ptr()) };
        if drive_type != 2 && drive_type != 3 {
            return Err("目标磁盘不是可用的本地磁盘，不能使用网络映射或不可用驱动器".to_owned());
        }
    }

    Ok(())
}

#[cfg(windows)]
unsafe extern "system" {
    #[link_name = "GetDriveTypeW"]
    fn get_drive_type_w(root: *const u16) -> u32;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn relative_path_is_rejected() {
        assert!(validate_local_archive_dir(Path::new("relative/archive")).is_err());
    }

    #[cfg(windows)]
    #[test]
    fn unc_network_path_is_rejected() {
        assert!(validate_local_archive_dir(Path::new(r"\\server\share\archive")).is_err());
    }

    #[cfg(windows)]
    #[test]
    fn default_path_points_to_d_drive_without_override() {
        if env::var_os("PAPER_ARCHIVE_DATA_DIR").is_none() {
            assert_eq!(default_archive_dir(), PathBuf::from(r"D:\PaperArchiveData"));
        }
    }
}
