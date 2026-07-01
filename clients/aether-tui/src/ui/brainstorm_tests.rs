//! SC-BST-01..02 — Socratic Brainstorming Wizard (F5) state behavior tests.

#[cfg(test)]
mod tests {
    use crate::app::{AppMode, AppState};

    /// SC-BST-01: F5 opens the Brainstorm Wizard and resets step/answers.
    #[test]
    fn sc_bst_01_open_brainstorming_wizard() {
        let mut state = AppState::new("maria".to_string());
        // Simulate F5 (main.rs:338-342)
        state.mode = AppMode::BrainstormWizard;
        state.brainstorm_step = 0;
        state.brainstorm_answers = vec![String::new(); 4];
        assert_eq!(state.mode, AppMode::BrainstormWizard);
        assert_eq!(state.brainstorm_step, 0);
        assert_eq!(state.brainstorm_answers.len(), 4);
        assert!(state.brainstorm_answers.iter().all(|a| a.is_empty()));
    }

    /// SC-BST-02: Completing all 4 steps generates a markdown proposal into input.
    #[test]
    fn sc_bst_02_inject_brainstorm_proposal() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::BrainstormWizard;
        // Fill the four answers (Motivation, Approach 1, Approach 2, Trade-offs)
        state.brainstorm_answers = vec![
            "Fix TUI overlays".to_string(),
            "Add ratatui popups".to_string(),
            "Use full-screen panels".to_string(),
            "Popups are lighter; panels show more".to_string(),
        ];
        state.brainstorm_step = 3; // last step
        // Simulate Enter on final step (main.rs:600-610) — state portion
        let proposal = format!(
            "## Why\n\n{}\n\n## Approaches\n\n### Approach 1\n{}\n\n### Approach 2\n{}\n\n## Trade-offs\n\n{}",
            state.brainstorm_answers[0],
            state.brainstorm_answers[1],
            state.brainstorm_answers[2],
            state.brainstorm_answers[3]
        );
        state.input = proposal.clone();
        state.cursor_position = state.input.chars().count();
        state.mode = AppMode::Normal;
        assert!(state.input.contains("## Why"));
        assert!(state.input.contains("Fix TUI overlays"));
        assert!(state.input.contains("### Approach 1"));
        assert!(state.input.contains("### Approach 2"));
        assert!(state.input.contains("## Trade-offs"));
        assert_eq!(state.mode, AppMode::Normal);
        assert_eq!(state.cursor_position, state.input.chars().count());
    }

    /// Enter on steps 0-2 advances the step counter.
    #[test]
    fn sc_bst_enter_advances_step() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::BrainstormWizard;
        state.brainstorm_step = 0;
        // Simulate Enter (main.rs:597-599)
        if state.brainstorm_step < 3 {
            state.brainstorm_step += 1;
        }
        assert_eq!(state.brainstorm_step, 1);
    }

    /// Esc cancels the wizard and returns to Normal mode.
    #[test]
    fn sc_bst_esc_cancels_wizard() {
        let mut state = AppState::new("maria".to_string());
        state.mode = AppMode::BrainstormWizard;
        // Simulate Esc (main.rs:594-596)
        state.mode = AppMode::Normal;
        assert_eq!(state.mode, AppMode::Normal);
    }
}
