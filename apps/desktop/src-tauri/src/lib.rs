//! Data-M Tauri shell.
//!
//! This first milestone intentionally exposes no destructive file operations.
//! All real asset mutations will go through validated application use cases.
mod asset_browser;
mod asset_lifecycle;
mod asset_recycle;
mod asset_transfer;
mod cas;
mod cas_audit;
mod domain_db;
mod drop_preview;
mod experiment_tree;
mod figure_export;
mod import_jobs;
mod paper_workspace;
mod project_tree;
mod provenance_trace;
mod repository_safety;
#[cfg(test)]
mod s1_ipc_tests;
mod statistical_analysis;
#[tauri::command]
fn shell_status() -> &'static str {
    "data-m-desktop-shell"
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            shell_status,
            asset_browser::list_assets,
            asset_browser::get_asset,
            asset_transfer::asset_transfer,
            asset_lifecycle::asset_lifecycle,
            asset_recycle::list_recycled_assets,
            drop_preview::inspect_drop_paths,
            project_tree::project_tree,
            experiment_tree::experiment_tree,
            import_jobs::import_jobs,
            repository_safety::repository_safety,
            statistical_analysis::statistical_analysis,
            paper_workspace::paper_workspace,
            provenance_trace::provenance_trace,
            figure_export::figure_export,
            figure_export::figure_export_cancel
        ])
        .run(tauri::generate_context!())
        .expect("failed to run Data-M desktop");
}

#[cfg(test)]
mod tests {
    use super::shell_status;

    #[test]
    fn s0_shell_status_identifies_non_destructive_shell() {
        assert_eq!(shell_status(), "data-m-desktop-shell");
    }
}
