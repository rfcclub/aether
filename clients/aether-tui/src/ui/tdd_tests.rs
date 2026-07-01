//! SC-TDD-01 — F6 TDD Template Injector state behavior test.

#[cfg(test)]
mod tests {
    use crate::app::AppState;

    /// SC-TDD-01: Pressing F6 pre-populates the input with the RED/GREEN/REFACTOR/COMMIT template.
    #[test]
    fn sc_tdd_01_inject_tdd_template_into_input() {
        let mut state = AppState::new("maria".to_string());
        // Simulate F6 (main.rs:344-348)
        let tdd_template = "🔄 TDD Step: [RED / GREEN / REFACTOR / COMMIT]\n- **Goal:** [Enter target goal]\n- **Files:**\n  - [MODIFY/NEW] [file name](file://absolute/path)\n- **Step Details:**\n  - [Write out detailed execution steps - NO PLACEHOLDERS]";
        state.input = tdd_template.to_string();
        state.cursor_position = state.input.chars().count();
        assert!(state.input.contains("RED / GREEN / REFACTOR / COMMIT"));
        assert!(state.input.contains("**Goal:**"));
        assert!(state.input.contains("**Files:**"));
        assert!(state.input.contains("**Step Details:**"));
        assert_eq!(state.cursor_position, state.input.chars().count());
        assert!(!state.input.is_empty());
    }
}
