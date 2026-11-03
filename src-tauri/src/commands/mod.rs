use tauri::State;

use crate::{
    archive::DataRecord,
    state::{AppState, ArchiveStatus},
};

#[tauri::command]
pub fn get_archive_status(state: State<'_, AppState>) -> Result<ArchiveStatus, String> {
    state.status()
}

#[tauri::command]
pub fn initialize_archive(
    path: Option<String>,
    state: State<'_, AppState>,
) -> Result<ArchiveStatus, String> {
    state.initialize(path)
}

#[tauri::command]
pub fn list_data_records(state: State<'_, AppState>) -> Result<Vec<DataRecord>, String> {
    state
        .store()?
        .list_data_records()
        .map_err(|err| format!("读取数据列表失败：{err}"))
}

#[tauri::command]
pub async fn import_data_file(
    path: String,
    state: State<'_, AppState>,
) -> Result<DataRecord, String> {
    if path.trim().is_empty() {
        return Err("请选择要导入的文件".to_owned());
    }

    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store
            .import_file(path)
            .map_err(|err| format!("文件导入失败：{err}"))
    })
    .await
    .map_err(|err| format!("文件导入任务未正常结束：{err}"))?
}
