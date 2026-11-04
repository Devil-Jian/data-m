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
            commands::import_data_file,
            commands::create_paper,
            commands::list_papers,
            commands::get_paper,
            commands::attach_paper_manuscript,
            commands::create_paper_item,
            commands::list_paper_items,
            commands::list_paper_resources,
            commands::link_data_to_item,
            commands::set_item_anchor,
            commands::get_data_details,
            commands::read_paper_document,
            commands::read_data_image,
            commands::scan_paper_folder,
            commands::import_paper_folder,
            commands::check_paper_archive,
            commands::finalize_paper_archive,
            commands::revise_archived_paper,
            commands::list_other_resources,
            commands::update_resource_classification,
            commands::classify_other_resource,
            commands::reveal_data_file
        ])
        .run(tauri::generate_context!())
        .expect("error while running paper archive application");
}
