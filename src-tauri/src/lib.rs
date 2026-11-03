pub mod archive;
mod commands;
mod state;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .manage(state::AppState::new())
        .invoke_handler(tauri::generate_handler![
            commands::get_archive_status,
            commands::initialize_archive,
            commands::list_data_records,
            commands::import_data_file
        ])
        .run(tauri::generate_context!())
        .expect("error while running paper archive application");
}
