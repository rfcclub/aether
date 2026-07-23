use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionState {
    pub agent: String,
    pub scroll_offset: usize,
    pub input_draft: String,
    pub context_files: Vec<String>,
    pub current_model: Option<String>,
    pub current_agent_selection: usize,
    pub saved_at: String,
}

pub fn session_dir() -> Option<PathBuf> {
    let home = std::env::var("HOME").ok()?;
    let dir = PathBuf::from(home).join(".aether/sessions");
    let _ = std::fs::create_dir_all(&dir);
    Some(dir)
}

pub fn save_session(state: &crate::app::AppState) {
    let Some(dir) = session_dir() else { return };
    let timestamp = chrono::Utc::now().format("%Y%m%d-%H%M%S").to_string();
    let filename = format!("{}-{}.json", state.group, timestamp);
    let path = dir.join(filename);

    let current_model = state.models.as_ref().map(|m| m.current.clone());

    let session = SessionState {
        agent: state.group.clone(),
        scroll_offset: state.scroll_offset,
        input_draft: state.input.clone(),
        context_files: state.context_files.clone(),
        current_model,
        current_agent_selection: state.agent_selection,
        saved_at: chrono::Utc::now().to_rfc3339(),
    };

    if let Ok(json) = serde_json::to_string_pretty(&session) {
        let _ = std::fs::write(path, json);
    }
}

pub fn load_latest_session(agent: &str) -> Option<SessionState> {
    let dir = session_dir()?;
    let mut entries: Vec<_> = std::fs::read_dir(&dir)
        .ok()?
        .filter_map(|e| e.ok())
        .filter(|e| {
            e.file_name()
                .to_str()
                .map(|n| n.starts_with(&format!("{}-", agent)) && n.ends_with(".json"))
                .unwrap_or(false)
        })
        .collect();

    entries.sort_by(|a, b| b.file_name().cmp(&a.file_name()));

    let latest = entries.first()?;
    let json = std::fs::read_to_string(latest.path()).ok()?;
    serde_json::from_str(&json).ok()
}

pub fn clear_old_sessions(agent: &str, keep: usize) {
    let Some(dir) = session_dir() else { return };
    let mut entries: Vec<_> = std::fs::read_dir(&dir)
        .ok()
        .into_iter()
        .flatten()
        .filter_map(|e| e.ok())
        .filter(|e| {
            e.file_name()
                .to_str()
                .map(|n| n.starts_with(&format!("{}-", agent)) && n.ends_with(".json"))
                .unwrap_or(false)
        })
        .collect();

    entries.sort_by(|a, b| b.file_name().cmp(&a.file_name()));

    for old in entries.into_iter().skip(keep) {
        let _ = std::fs::remove_file(old.path());
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::app::AppState;

    #[test]
    fn test_session_dir_creates_directory() {
        let dir = session_dir();
        assert!(dir.is_some());
        assert!(dir.unwrap().exists());
    }

    #[test]
    fn test_save_and_load_session() {
        let mut state = AppState::new("test-agent".to_string());
        state.scroll_offset = 42;
        state.input = "test input".to_string();
        state.context_files = vec!["file1.txt".to_string(), "file2.txt".to_string()];
        state.agent_selection = 3;

        save_session(&state);

        let loaded = load_latest_session("test-agent");
        assert!(loaded.is_some());
        let loaded = loaded.unwrap();
        assert_eq!(loaded.agent, "test-agent");
        assert_eq!(loaded.scroll_offset, 42);
        assert_eq!(loaded.input_draft, "test input");
        assert_eq!(loaded.context_files, vec!["file1.txt", "file2.txt"]);
        assert_eq!(loaded.current_agent_selection, 3);
    }

    #[test]
    fn test_load_nonexistent_session() {
        let loaded = load_latest_session("nonexistent-agent-xyz");
        assert!(loaded.is_none());
    }

    #[test]
    fn test_clear_old_sessions() {
        let agent = "test-clear-agent";

        for i in 0..5 {
            let mut state = AppState::new(agent.to_string());
            state.input = format!("input {}", i);
            save_session(&state);
            std::thread::sleep(std::time::Duration::from_millis(10));
        }

        clear_old_sessions(agent, 2);

        let dir = session_dir().unwrap();
        let remaining: Vec<_> = std::fs::read_dir(&dir)
            .unwrap()
            .filter_map(|e| e.ok())
            .filter(|e| {
                e.file_name()
                    .to_str()
                    .map(|n| n.starts_with(&format!("{}-", agent)) && n.ends_with(".json"))
                    .unwrap_or(false)
            })
            .collect();

        assert!(remaining.len() <= 2);
    }
}
