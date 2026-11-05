use tauri::{State, ipc::Response};

use crate::{
    archive::{
        DataRecord,
        papers::{DataDetails, ItemAnchor, Paper, PaperItem, PaperResource},
    },
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

#[tauri::command]
pub fn create_paper(
    title: String,
    doi: Option<String>,
    state: State<'_, AppState>,
) -> Result<Paper, String> {
    state
        .store()?
        .create_paper(&title, doi.as_deref())
        .map_err(|error| format!("创建论文失败：{error}"))
}

#[tauri::command]
pub fn list_papers(state: State<'_, AppState>) -> Result<Vec<Paper>, String> {
    state
        .store()?
        .list_papers()
        .map_err(|error| format!("加载论文目录失败：{error}"))
}

#[tauri::command]
pub fn get_paper(paper_id: String, state: State<'_, AppState>) -> Result<Paper, String> {
    state
        .store()?
        .get_paper(&paper_id)
        .map_err(|error| format!("加载论文失败：{error}"))
}

#[tauri::command]
pub async fn attach_paper_manuscript(
    paper_id: String,
    path: String,
    state: State<'_, AppState>,
) -> Result<Paper, String> {
    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store
            .attach_manuscript(&paper_id, path)
            .map_err(|error| format!("导入论文正文失败：{error}"))
    })
    .await
    .map_err(|error| format!("论文导入任务中断：{error}"))?
}

#[tauri::command]
pub fn create_paper_item(
    paper_id: String,
    item_type: String,
    label: String,
    parent_id: Option<String>,
    state: State<'_, AppState>,
) -> Result<PaperItem, String> {
    state
        .store()?
        .create_paper_item(&paper_id, &item_type, &label, parent_id.as_deref())
        .map_err(|error| format!("创建 Figure / Panel / Table 失败：{error}"))
}

#[tauri::command]
pub fn list_paper_items(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<Vec<PaperItem>, String> {
    state
        .store()?
        .list_paper_items(&paper_id)
        .map_err(|error| format!("加载图表列表失败：{error}"))
}

#[tauri::command]
pub fn list_paper_resources(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<Vec<PaperResource>, String> {
    state
        .store()?
        .list_paper_resources(&paper_id)
        .map_err(|error| format!("加载论文原始数据失败：{error}"))
}

#[tauri::command]
pub fn link_data_to_item(
    paper_id: String,
    item_id: String,
    data_id: String,
    state: State<'_, AppState>,
) -> Result<String, String> {
    state
        .store()?
        .attach_data_to_item(&paper_id, &item_id, &data_id)
        .map_err(|error| format!("关联原始数据失败：{error}"))
}

#[tauri::command]
pub fn set_item_anchor(
    paper_id: String,
    item_id: String,
    anchor: ItemAnchor,
    state: State<'_, AppState>,
) -> Result<(), String> {
    state
        .store()?
        .set_item_anchor(&paper_id, &item_id, anchor)
        .map_err(|error| format!("图表定位失败：{error}"))
}

#[tauri::command]
pub fn get_data_details(
    data_id: String,
    state: State<'_, AppState>,
) -> Result<DataDetails, String> {
    state
        .store()?
        .get_data_details(&data_id)
        .map_err(|error| format!("读取文件信息失败：{error}"))
}

#[tauri::command]
pub async fn read_paper_document(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<Response, String> {
    let store = state.store()?;
    let bytes = tauri::async_runtime::spawn_blocking(move || {
        store
            .read_paper_document(&paper_id)
            .map_err(|error| format!("读取论文预览失败：{error}"))
    })
    .await
    .map_err(|error| format!("预览任务中断：{error}"))??;
    Ok(Response::new(bytes))
}

#[tauri::command]
pub async fn read_data_image(
    data_id: String,
    state: State<'_, AppState>,
) -> Result<Response, String> {
    let store = state.store()?;
    let bytes = tauri::async_runtime::spawn_blocking(move || {
        store
            .read_data_image(&data_id)
            .map_err(|error| format!("读取预览图失败：{error}"))
    })
    .await
    .map_err(|error| format!("预览任务中断：{error}"))??;
    Ok(Response::new(bytes))
}

#[tauri::command]
pub fn reveal_data_file(data_id: String, state: State<'_, AppState>) -> Result<(), String> {
    state
        .store()?
        .reveal_data_file(&data_id)
        .map_err(|error| format!("无法打开原始文件目录：{error}"))
}

#[tauri::command]
pub async fn scan_paper_folder(
    path: String,
    state: State<'_, AppState>,
) -> Result<crate::archive::workflow::FolderScan, String> {
    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store
            .scan_folder(std::path::Path::new(&path))
            .map_err(|err| format!("扫描论文文件夹失败：{err}"))
    })
    .await
    .map_err(|err| format!("扫描任务意外结束：{err}"))?
}

#[tauri::command]
pub async fn import_paper_folder(
    path: String,
    state: State<'_, AppState>,
) -> Result<crate::archive::workflow::ImportResult, String> {
    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store
            .import_folder(std::path::Path::new(&path))
            .map_err(|err| format!("导入整篇论文失败：{err}"))
    })
    .await
    .map_err(|err| format!("导入任务意外结束：{err}"))?
}

#[tauri::command]
pub async fn check_paper_archive(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<crate::archive::workflow::ArchiveCheck, String> {
    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store.check_paper(&paper_id).map_err(|err| err.to_string())
    })
    .await
    .map_err(|err| format!("归档检查任务结束异常：{err}"))?
}

#[tauri::command]
pub async fn finalize_paper_archive(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<Paper, String> {
    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store
            .finalize_paper(&paper_id)
            .map_err(|err| err.to_string())
    })
    .await
    .map_err(|err| format!("归档任务结束异常：{err}"))?
}

#[tauri::command]
pub fn revise_archived_paper(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<Paper, String> {
    state
        .store()?
        .start_paper_revision(&paper_id)
        .map_err(|err| err.to_string())
}

#[tauri::command]
pub fn list_other_resources(
    paper_id: String,
    state: State<'_, AppState>,
) -> Result<Vec<crate::archive::workflow::OtherResource>, String> {
    state
        .store()?
        .list_other_resources(&paper_id)
        .map_err(|err| err.to_string())
}

#[tauri::command]
pub fn update_resource_classification(
    paper_id: String,
    reference_id: String,
    target_item_id: String,
    role: String,
    state: State<'_, AppState>,
) -> Result<(), String> {
    state
        .store()?
        .update_resource(&paper_id, &reference_id, &target_item_id, &role)
        .map_err(|err| err.to_string())
}

#[tauri::command]
pub fn classify_other_resource(
    paper_id: String,
    data_id: String,
    target_item_id: String,
    role: String,
    state: State<'_, AppState>,
) -> Result<(), String> {
    state
        .store()?
        .move_other_to_item(&paper_id, &data_id, &target_item_id, &role)
        .map_err(|err| err.to_string())
}

#[tauri::command]
pub async fn import_paper_plan(
    path: String,
    edits: Vec<crate::archive::workflow::PlannedFile>,
    state: State<'_, AppState>,
) -> Result<crate::archive::workflow::ImportResult, String> {
    let store = state.store()?;
    tauri::async_runtime::spawn_blocking(move || {
        store
            .import_folder_with_plan(std::path::Path::new(&path), edits)
            .map_err(|error| format!("批量归档失败：{error}"))
    })
    .await
    .map_err(|error| format!("归档任务意外结束：{error}"))?
}
