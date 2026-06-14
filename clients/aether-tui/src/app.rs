use crate::events::{AppEvent, Message, ModelsPayload, Role};
use chrono::Utc;

#[derive(Debug, Clone, PartialEq)]
pub enum AppMode {
    Connecting,
    Normal,
    Scroll,
    ModelPicker,
    AgentPicker,
    ShowHelp,
    ContextManager,
    BrainstormWizard,
    GitDashboard,
}

pub struct AppState {
    pub mode: AppMode,
    pub messages: Vec<Message>,
    pub streaming_buf: String,
    pub input: String,
    pub is_typing: bool,
    pub connected: bool,
    pub reconnect_hint: Option<String>,
    pub group: String,
    // Phase 2 fields
    pub models: Option<ModelsPayload>,
    pub scroll_offset: usize,
    pub history_loaded: bool,
    pub picker_selection: usize,
    // Fancy upgrades
    pub agents: Vec<(String, String, String)>, // (name, display_name, emoji)
    pub agent_selection: usize,
    pub spinner_frame: u8,
    pub tokens_received: usize,
    // Overlay state fields
    pub context_files: Vec<String>,
    pub context_selection: usize,
    pub show_input_dialog: bool,
    pub dialog_input: String,
    pub git_files: Vec<(String, String)>, // (path, status)
    pub git_selection: usize,
    pub selected_diff: String,
    pub brainstorm_step: usize,
    pub brainstorm_answers: Vec<String>,
    pub cursor_position: usize,
}

impl AppState {
    pub fn new(group: String) -> Self {
        Self {
            mode: AppMode::Connecting,
            messages: Vec::new(),
            streaming_buf: String::new(),
            input: String::new(),
            is_typing: false,
            connected: false,
            reconnect_hint: None,
            group,
            models: None,
            scroll_offset: 0,
            history_loaded: false,
            picker_selection: 0,
            agents: Vec::new(),
            agent_selection: 0,
            spinner_frame: 0,
            tokens_received: 0,
            context_files: Vec::new(),
            context_selection: 0,
            show_input_dialog: false,
            dialog_input: String::new(),
            git_files: Vec::new(),
            git_selection: 0,
            selected_diff: String::new(),
            brainstorm_step: 0,
            brainstorm_answers: vec![String::new(); 4],
            cursor_position: 0,
        }
    }

    pub fn move_cursor_left(&mut self) {
        self.cursor_position = self.cursor_position.saturating_sub(1);
    }

    pub fn move_cursor_right(&mut self) {
        if self.cursor_position < self.input.chars().count() {
            self.cursor_position += 1;
        }
    }

    pub fn move_cursor_up(&mut self) {
        let mut char_line_starts = vec![0];
        let mut char_count = 0;
        for c in self.input.chars() {
            char_count += 1;
            if c == '\n' {
                char_line_starts.push(char_count);
            }
        }

        let mut current_line_idx = 0;
        for (idx, &start) in char_line_starts.iter().enumerate() {
            if self.cursor_position >= start {
                current_line_idx = idx;
            } else {
                break;
            }
        }

        if current_line_idx > 0 {
            let current_col = self.cursor_position - char_line_starts[current_line_idx];
            let prev_line_start = char_line_starts[current_line_idx - 1];
            let prev_line_len = char_line_starts[current_line_idx] - prev_line_start - 1;
            let target_col = current_col.min(prev_line_len);
            self.cursor_position = prev_line_start + target_col;
        }
    }

    pub fn move_cursor_down(&mut self) {
        let mut char_line_starts = vec![0];
        let mut char_count = 0;
        for c in self.input.chars() {
            char_count += 1;
            if c == '\n' {
                char_line_starts.push(char_count);
            }
        }

        let mut current_line_idx = 0;
        for (idx, &start) in char_line_starts.iter().enumerate() {
            if self.cursor_position >= start {
                current_line_idx = idx;
            } else {
                break;
            }
        }

        if current_line_idx + 1 < char_line_starts.len() {
            let current_col = self.cursor_position - char_line_starts[current_line_idx];
            let next_line_start = char_line_starts[current_line_idx + 1];
            let next_line_end = if current_line_idx + 2 < char_line_starts.len() {
                char_line_starts[current_line_idx + 2] - 1
            } else {
                self.input.chars().count()
            };
            let next_line_len = next_line_end - next_line_start;
            let target_col = current_col.min(next_line_len);
            self.cursor_position = next_line_start + target_col;
        }
    }

    pub fn insert_char(&mut self, c: char) {
        let char_len = self.input.chars().count();
        if self.cursor_position > char_len {
            self.cursor_position = char_len;
        }
        let byte_idx = self.input.char_indices().map(|(b_idx, _)| b_idx).nth(self.cursor_position).unwrap_or(self.input.len());
        self.input.insert(byte_idx, c);
        self.cursor_position += 1;
    }

    pub fn delete_backspace(&mut self) {
        if self.cursor_position > 0 {
            let char_len = self.input.chars().count();
            if self.cursor_position > char_len {
                self.cursor_position = char_len;
            }
            let byte_idx = self.input.char_indices().map(|(b_idx, _)| b_idx).nth(self.cursor_position - 1).unwrap();
            self.input.remove(byte_idx);
            self.cursor_position -= 1;
        }
    }

    pub fn delete_char(&mut self) {
        let char_len = self.input.chars().count();
        if self.cursor_position < char_len {
            let byte_idx = self.input.char_indices().map(|(b_idx, _)| b_idx).nth(self.cursor_position).unwrap();
            self.input.remove(byte_idx);
        }
    }

    pub fn load_agents(&mut self) {
        self.agents = crate::config::load_available_agents();
    }

    pub fn handle_event(&mut self, event: AppEvent) -> bool {
        match event {
            AppEvent::Connected => {
                self.connected = true;
                self.mode = AppMode::Normal;
                self.reconnect_hint = None;
            }
            AppEvent::Disconnected(reason) => {
                self.connected = false;
                self.mode = AppMode::Connecting;
                self.reconnect_hint = Some(reason);
            }
            AppEvent::StreamChunk(text) => {
                self.streaming_buf.push_str(&text);
                self.tokens_received += 1;
            }
            AppEvent::MessageComplete(text) => {
                if !self.streaming_buf.is_empty() {
                    self.messages.push(Message {
                        role: Role::Assistant,
                        content: std::mem::take(&mut self.streaming_buf),
                        timestamp: Utc::now(),
                        is_historical: false,
                    });
                } else {
                    self.messages.push(Message {
                        role: Role::Assistant,
                        content: text,
                        timestamp: Utc::now(),
                        is_historical: false,
                    });
                }
                self.is_typing = false;
            }
            AppEvent::Typing(state) => {
                self.is_typing = state;
            }
            AppEvent::BackendError(err) => {
                self.messages.push(Message {
                    role: Role::Assistant,
                    content: format!("⚠ Error: {}", err),
                    timestamp: Utc::now(),
                    is_historical: false,
                });
            }
            AppEvent::Quit => return true,
            AppEvent::HistoryLoaded(msgs) => {
                if !self.history_loaded {
                    let mut combined = msgs;
                    combined.append(&mut self.messages);
                    self.messages = combined;
                    self.history_loaded = true;
                }
            }
            AppEvent::ModelsLoaded(payload) => {
                self.models = payload;
            }
            AppEvent::GitStatusLoaded(files) => {
                self.git_files = files;
                self.git_selection = 0;
            }
            AppEvent::GitDiffLoaded(diff) => {
                self.selected_diff = diff;
            }
        }
        false
    }

    pub fn send_message(&mut self) -> Option<String> {
        if !self.history_loaded { return None; }
        let text = self.input.trim().to_string();
        if text.is_empty() { return None; }
        self.input.clear();
        self.cursor_position = 0;
        self.tokens_received = 0;
        self.messages.push(Message {
            role: Role::User,
            content: text.clone(),
            timestamp: Utc::now(),
            is_historical: false,
        });
        let json = serde_json::json!({
            "type": "message",
            "text": text,
            "group": self.group,
        });
        Some(json.to_string())
    }

    /// Return total count of selectable models across all providers
    pub fn total_selectable_models(&self) -> usize {
        if let Some(ref models) = self.models {
            models.providers.iter().map(|p| p.models.len()).sum()
        } else {
            0
        }
    }

    /// Return the model name at picker_selection index (flat index across providers)
    pub fn selected_model(&self) -> Option<String> {
        if let Some(ref models) = self.models {
            let mut idx = 0;
            for provider in &models.providers {
                for model in &provider.models {
                    if idx == self.picker_selection {
                        return Some(model.clone());
                    }
                    idx += 1;
                }
            }
        }
        None
    }
}
