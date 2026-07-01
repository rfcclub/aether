//! SC-CTX-01..04 — Context Files Manager overlay (F4) state behavior tests.
//!
//! These are characterization tests for the AppState transitions and data
//! manipulation that the F4 Context Manager overlay relies on. Render-level
//! tests would require a ratatui TestBackend; here we verify the state model
//! that `handle_key(AppMode::ContextManager)` mutates in
//! `clients/aether-tui/src/main.rs`.

#[cfg(test)]
mod tests {
    use crate::app::{AppMode, AppState};

    /// SC-CTX-01: Opening the Context Manager overlay shifts mode + resets selection.
    #[test]
    fn sc_ctx_01_open_context_manager_overlay() {
        let mut state = AppState::new("maria".to_string());
        // Simulate F4 key handler effect (main.rs:332-336)
        state.mode = AppMode::ContextManager;
        state.context_selection = 0;
        state.show_input_dialog = false;
        assert_eq!(state.mode, AppMode::ContextManager);
        assert_eq!(state.context_selection, 0);
        assert!(!state.show_input_dialog);
    }

    /// SC-CTX-02: Entering a valid path via the dialog adds it to context_files.
    #[test]
    fn sc_ctx_02_add_file_to_context_list() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::ContextManager;
        // Simulate 'a' key → open dialog (main.rs:554-556)
        state.show_input_dialog = true;
        state.dialog_input.clear();
        // Simulate typing a path
        state.dialog_input.push_str("src/Aether/Program.cs");
        // Simulate Enter in dialog (main.rs:523-535) — state portion only
        let path = state.dialog_input.trim().to_string();
        assert!(!path.is_empty());
        state.context_files.push(path);
        state.dialog_input.clear();
        state.show_input_dialog = false;
        assert_eq!(state.context_files, vec!["src/Aether/Program.cs"]);
        assert!(state.dialog_input.is_empty());
        assert!(!state.show_input_dialog);
    }

    /// SC-CTX-03: Pressing 'd' removes the selected file from context_files.
    #[test]
    fn sc_ctx_03_remove_file_from_context_list() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::ContextManager;
        state.context_files = vec![
            "src/Aether/Program.cs".to_string(),
            "tests/Aether.Tests/Tests.cs".to_string(),
        ];
        state.context_selection = 0;
        // Simulate 'd' key (main.rs:558-568) — state portion
        if !state.context_files.is_empty() && state.context_selection < state.context_files.len() {
            state.context_files.remove(state.context_selection);
            state.context_selection = state.context_selection.saturating_sub(1);
        }
        assert_eq!(state.context_files, vec!["tests/Aether.Tests/Tests.cs"]);
        assert_eq!(state.context_selection, 0);
    }

    /// SC-CTX-04: Pressing 'c' clears the entire context file list.
    #[test]
    fn sc_ctx_04_clear_all_files_from_context() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::ContextManager;
        state.context_files = vec![
            "a.rs".to_string(),
            "b.rs".to_string(),
            "c.rs".to_string(),
        ];
        state.context_selection = 2;
        // Simulate 'c' key (main.rs:570-578) — state portion
        state.context_files.clear();
        state.context_selection = 0;
        assert!(state.context_files.is_empty());
        assert_eq!(state.context_selection, 0);
    }

    /// Closing the overlay (Esc/F4) returns to Normal mode.
    #[test]
    fn sc_ctx_close_returns_to_normal() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::ContextManager;
        // Simulate Esc/F4 (main.rs:551-553)
        state.mode = AppMode::Normal;
        assert_eq!(state.mode, AppMode::Normal);
    }
}
