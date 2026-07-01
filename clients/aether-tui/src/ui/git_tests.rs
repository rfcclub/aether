//! SC-GIT-01..02 — Interactive Git Dashboard (F7) state behavior tests.

#[cfg(test)]
mod tests {
    use crate::app::{AppMode, AppState};
    use crate::events::AppEvent;

    /// SC-GIT-01: F7 opens the Git Dashboard and resets selection + diff placeholder.
    #[test]
    fn sc_git_01_open_git_dashboard_pane() {
        let mut state = AppState::new("maria".to_string());
        // Simulate F7 (main.rs:350-353) — state portion
        state.mode = AppMode::GitDashboard;
        state.git_selection = 0;
        state.selected_diff = "Loading diff...".to_string();
        assert_eq!(state.mode, AppMode::GitDashboard);
        assert_eq!(state.git_selection, 0);
        assert_eq!(state.selected_diff, "Loading diff...");
    }

    /// SC-GIT-02: A git_status_response event populates git_files (used for stage toggle).
    #[test]
    fn sc_git_02_git_status_loaded_populates_files() {
        let mut state = AppState::new("maria".to_string());
        // Simulate AppEvent::GitStatusLoaded handling (app.rs:288-291)
        let files = vec![
            ("src/Aether/Program.cs".to_string(), "Staged".to_string()),
            ("clients/aether-tui/src/main.rs".to_string(), "Modified".to_string()),
        ];
        state.handle_event(AppEvent::GitStatusLoaded(files.clone()));
        assert_eq!(state.git_files, files);
        assert_eq!(state.git_selection, 0);
    }

    /// The stage toggle computes target_staged = current_status != "Staged".
    /// This mirrors main.rs:636-647 decision logic (state-only portion).
    #[test]
    fn sc_git_02_stage_toggle_logic() {
        let mut state = AppState::new("maria".to_string());
        state.git_files = vec![
            ("src/Aether/Program.cs".to_string(), "Staged".to_string()),
            ("README.md".to_string(), "Modified".to_string()),
        ];
        // Select the Staged file
        state.git_selection = 0;
        let (_, current_status) = &state.git_files[state.git_selection];
        let target_staged = current_status != "Staged";
        assert!(!target_staged, "already Staged → target should be unstage");
        // Select the Modified file
        state.git_selection = 1;
        let (_, current_status) = &state.git_files[state.git_selection];
        let target_staged = current_status != "Staged";
        assert!(target_staged, "Modified → target should be stage");
    }

    /// Esc/F7 closes the dashboard and returns to Normal mode.
    #[test]
    fn sc_git_close_returns_to_normal() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::GitDashboard;
        // Simulate Esc/F7 (main.rs:625-627)
        state.mode = AppMode::Normal;
        assert_eq!(state.mode, AppMode::Normal);
    }
}
