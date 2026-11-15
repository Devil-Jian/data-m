//! Data-M Tauri shell.
//!
//! This first milestone intentionally exposes no destructive file operations.
//! All real asset mutations will go through validated application use cases.
mod cas;
mod cas_audit;
mod domain_db;
mod drop_preview;
mod experiment_tree;
mod project_tree;
#[cfg(test)]
mod s1_ipc_tests;
#[tauri::command]
fn shell_status() -> &'static str {
    "data-m-desktop-shell"
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            shell_status,
            drop_preview::inspect_drop_paths,
            project_tree::project_tree,
            experiment_tree::experiment_tree
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
