//! Read-only inspection of a native Explorer drop.
//!
//! No source path is returned to the webview and no file is opened, copied,
//! traversed or modified. The preview is not an import authorization.
use serde::Serialize;
use std::fs;
use std::path::Path;

const MAX_PREVIEW_ENTRIES: usize = 100;
const MAX_PATH_LENGTH: usize = 32_768;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DropEntry {
    pub name: String,
    pub kind: &'static str,
    pub size_bytes: Option<u64>,
    pub eligible: bool,
    pub warning: Option<&'static str>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DropPreview {
    pub entries: Vec<DropEntry>,
    pub total_received: usize,
    pub omitted_count: usize,
    pub eligible_count: usize,
}

fn inspect_one(raw_path: &str) -> DropEntry {
    // Never echo absolute paths or operating-system error strings to the UI.
    let path = Path::new(raw_path);
    let name = path
        .file_name()
        .and_then(|filename| filename.to_str())
        .filter(|name| !name.is_empty())
        .unwrap_or("未命名项目")
        .to_string();

    let rejected = |kind, warning| DropEntry {
        name: name.clone(),
        kind,
        size_bytes: None,
        eligible: false,
        warning: Some(warning),
    };
    if raw_path.is_empty() || raw_path.len() > MAX_PATH_LENGTH || !path.is_absolute() {
        return rejected("invalid", "路径无效");
    }
    match fs::symlink_metadata(path) {
        Ok(meta) if meta.file_type().is_symlink() => {
            rejected("link", "符号链接暂不支持导入")
        }
        Ok(meta) if meta.is_file() => DropEntry {
            name,
            kind: "file",
            size_bytes: Some(meta.len()),
            eligible: true,
            warning: None,
        },
        Ok(meta) if meta.is_dir() => DropEntry {
            name,
            kind: "directory",
            size_bytes: None,
            eligible: true,
            warning: None,
        },
        Ok(_) => rejected("unsupported", "不支持的文件类型"),
        Err(_) => rejected("unavailable", "无法读取文件信息"),
    }
}

/// Read-only: `paths` originates from a Tauri native drag/drop event.
/// No recursive traversal, hashing, copying or file creation occurs here.
#[tauri::command]
pub fn inspect_drop_paths(paths: Vec<String>) -> DropPreview {
    let total_received = paths.len();
    let entries: Vec<DropEntry> = paths
        .iter()
        .take(MAX_PREVIEW_ENTRIES)
        .map(|path| inspect_one(path))
        .collect();
    let eligible_count = entries.iter().filter(|entry| entry.eligible).count();
    DropPreview {
        entries,
        total_received,
        omitted_count: total_received.saturating_sub(MAX_PREVIEW_ENTRIES),
        eligible_count,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn empty_and_relative_paths_are_not_eligible() {
        let preview = inspect_drop_paths(vec!["".into(), "../relative.tif".into()]);
        assert_eq!(preview.total_received, 2);
        assert_eq!(preview.eligible_count, 0);
        assert!(preview.entries.iter().all(|entry| !entry.eligible));
    }

    #[test]
    fn inspection_returns_only_basename_and_never_modifies_source() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("monotonic test time")
            .as_nanos();
        let root = std::env::temp_dir().join(format!("data-m-drop-{}-{unique}", std::process::id()));
        fs::create_dir(&root).expect("create isolated test directory");
        let file = root.join("source.tif");
        fs::write(&file, b"original scientific data").expect("create isolated fixture");
        let preview = inspect_drop_paths(vec![
            file.to_string_lossy().to_string(),
            root.to_string_lossy().to_string(),
        ]);
        assert_eq!(preview.total_received, 2);
        assert_eq!(preview.eligible_count, 2);
        assert_eq!(preview.entries[0].name, "source.tif");
        assert_eq!(preview.entries[0].kind, "file");
        assert_eq!(preview.entries[0].size_bytes, Some(24));
        assert_eq!(preview.entries[1].kind, "directory");
        assert_eq!(preview.entries[1].size_bytes, None);
        assert_eq!(fs::read(&file).unwrap(), b"original scientific data");
        let serialized = serde_json::to_string(&preview).unwrap();
        assert!(!serialized.contains(root.to_string_lossy().as_ref()));
        fs::remove_file(file).unwrap();
        fs::remove_dir(root).unwrap();
    }

    #[test]
    fn limits_preview_count_without_traversing_extra_paths() {
        let paths = (0..105).map(|i| format!("relative-path-{i}")).collect();
        let preview = inspect_drop_paths(paths);
        assert_eq!(preview.entries.len(), 100);
        assert_eq!(preview.total_received, 105);
        assert_eq!(preview.omitted_count, 5);
    }

    #[cfg(unix)]
    #[test]
    fn rejects_symlinks_instead_of_following_them() {
        let link = std::env::temp_dir().join(format!("data-m-link-{}", std::process::id()));
        let _ = fs::remove_file(&link);
        std::os::unix::fs::symlink(std::env::temp_dir(), &link).unwrap();
        let entry = inspect_one(link.to_str().unwrap());
        assert_eq!(entry.kind, "link");
        assert!(!entry.eligible);
        fs::remove_file(link).unwrap();
    }
}
