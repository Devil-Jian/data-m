//! Data-M Tauri shell.
//!
//! This first milestone intentionally exposes no destructive file operations.
//! All real asset mutations will go through validated application use cases.
#[tauri::command]
fn shell_status() -> &'static str {
    "data-m-desktop-shell"
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![shell_status])
        .run(tauri::generate_context!())
        .expect("failed to run Data-M desktop");
}

#[cfg(test)]
mod tests {
    use super::shell_status;

    #[test]
    fn shell_status_identifies_non_destructive_shell() {
        assert_eq!(shell_status(), "data-m-desktop-shell");
    }
}
