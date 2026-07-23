use crossterm::event::{KeyCode, KeyModifiers};
use serde::Deserialize;
use std::path::PathBuf;

#[derive(Debug, Clone)]
pub struct Config {
    pub ws_url: String,
    pub group: String,
    pub ui: UiPrefs,
}

#[derive(Debug, Clone)]
pub struct UiPrefs {
    pub waiting_phrases: Vec<String>,
    pub default_waiting_phrase: String,
    pub default_agent: AgentDefaults,
    pub keybindings: KeyBindings,
}

#[derive(Debug, Clone)]
pub struct AgentDefaults {
    pub name: String,
    pub display_name: String,
    pub emoji: String,
}

impl Default for UiPrefs {
    fn default() -> Self {
        Self {
            waiting_phrases: default_waiting_phrases(),
            default_waiting_phrase: "Running ....".to_string(),
            default_agent: AgentDefaults::default(),
            keybindings: KeyBindings::default(),
        }
    }
}

impl Default for AgentDefaults {
    fn default() -> Self {
        Self {
            name: "maria".to_string(),
            display_name: "Maria".to_string(),
            emoji: "🌸".to_string(),
        }
    }
}

#[derive(Debug, Clone)]
pub struct KeyBindings {
    pub quit: (KeyCode, KeyModifiers),
    pub clear: (KeyCode, KeyModifiers),
    pub help: (KeyCode, KeyModifiers),
    pub model_picker: (KeyCode, KeyModifiers),
    pub agent_picker: (KeyCode, KeyModifiers),
    pub context_manager: (KeyCode, KeyModifiers),
    pub brainstorm: (KeyCode, KeyModifiers),
    pub tdd_template: (KeyCode, KeyModifiers),
    pub git_dashboard: (KeyCode, KeyModifiers),
    pub goals_dashboard: (KeyCode, KeyModifiers),
    pub skills_panel: (KeyCode, KeyModifiers),
    pub metrics_dashboard: (KeyCode, KeyModifiers),
    pub scroll_mode: (KeyCode, KeyModifiers),
}

impl Default for KeyBindings {
    fn default() -> Self {
        Self {
            quit: (KeyCode::Char('q'), KeyModifiers::CONTROL),
            clear: (KeyCode::Char('l'), KeyModifiers::CONTROL),
            help: (KeyCode::F(1), KeyModifiers::NONE),
            model_picker: (KeyCode::F(2), KeyModifiers::NONE),
            agent_picker: (KeyCode::F(3), KeyModifiers::NONE),
            context_manager: (KeyCode::F(4), KeyModifiers::NONE),
            brainstorm: (KeyCode::F(5), KeyModifiers::NONE),
            tdd_template: (KeyCode::F(6), KeyModifiers::NONE),
            git_dashboard: (KeyCode::F(7), KeyModifiers::NONE),
            goals_dashboard: (KeyCode::F(8), KeyModifiers::NONE),
            skills_panel: (KeyCode::F(9), KeyModifiers::NONE),
            metrics_dashboard: (KeyCode::F(10), KeyModifiers::NONE),
            scroll_mode: (KeyCode::Esc, KeyModifiers::NONE),
        }
    }
}

pub fn parse_key(s: &str) -> Option<(KeyCode, KeyModifiers)> {
    let s = s.trim().to_lowercase();
    let parts: Vec<&str> = s.split('+').collect();

    let mut mods = KeyModifiers::NONE;
    let mut key_part = s.as_str();

    if parts.len() == 2 {
        match parts[0].trim() {
            "c" | "ctrl" => mods = KeyModifiers::CONTROL,
            "s" | "shift" => mods = KeyModifiers::SHIFT,
            "a" | "alt" => mods = KeyModifiers::ALT,
            _ => return None,
        }
        key_part = parts[1].trim();
    }

    let code = match key_part {
        "esc" | "escape" => KeyCode::Esc,
        "enter" | "return" => KeyCode::Enter,
        "tab" => KeyCode::Tab,
        "backspace" | "bs" => KeyCode::Backspace,
        "delete" | "del" => KeyCode::Delete,
        "up" => KeyCode::Up,
        "down" => KeyCode::Down,
        "left" => KeyCode::Left,
        "right" => KeyCode::Right,
        "home" => KeyCode::Home,
        "end" => KeyCode::End,
        "pageup" | "pgup" => KeyCode::PageUp,
        "pagedown" | "pgdn" => KeyCode::PageDown,
        "f1" => KeyCode::F(1),
        "f2" => KeyCode::F(2),
        "f3" => KeyCode::F(3),
        "f4" => KeyCode::F(4),
        "f5" => KeyCode::F(5),
        "f6" => KeyCode::F(6),
        "f7" => KeyCode::F(7),
        "f8" => KeyCode::F(8),
        "f9" => KeyCode::F(9),
        "f10" => KeyCode::F(10),
        "f11" => KeyCode::F(11),
        "f12" => KeyCode::F(12),
        "space" => KeyCode::Char(' '),
        s if s.len() == 1 => KeyCode::Char(s.chars().next().unwrap()),
        _ => return None,
    };

    Some((code, mods))
}

pub fn default_waiting_phrases() -> Vec<String> {
    vec![
        "Thinking...".to_string(),
        "Stoking the cosmic forge...".to_string(),
        "Summoning Athanor flames...".to_string(),
        "Formulating answers...".to_string(),
        "Flabbergasting...".to_string(),
        "Calibrating synapse arrays...".to_string(),
        "Consulting the digital oracle...".to_string(),
        "Untangling quantum threads...".to_string(),
        "Injecting high-density thoughts...".to_string(),
        "Re-igniting the atomic core...".to_string(),
        "Whispering to the compiler...".to_string(),
    ]
}

#[derive(Deserialize, Default)]
struct AetherConfig {
    agents: Option<std::collections::HashMap<String, AgentEntry>>,
    ui: Option<UiSection>,
    default_agent: Option<DefaultAgentSection>,
}

#[derive(Deserialize)]
struct AgentEntry {
    workspace: Option<String>,
}

#[derive(Deserialize)]
struct RawAgentEntry {
    _workspace: Option<String>,
    #[serde(rename = "displayName")]
    display_name: Option<String>,
    emoji: Option<String>,
}

#[derive(Deserialize, Default)]
struct WorkspaceConfig {
    websocket: Option<WebsocketSection>,
    port: Option<u16>,
}

#[derive(Deserialize)]
struct WebsocketSection {
    port: Option<u16>,
}

#[derive(Deserialize, Default)]
struct UiSection {
    waiting_phrases: Option<Vec<String>>,
    #[serde(rename = "default_waiting_phrase")]
    default_waiting_phrase: Option<String>,
    #[serde(rename = "theme")]
    theme: Option<String>,
    keybindings: Option<KeybindingsSection>,
}

#[derive(Deserialize, Default)]
struct KeybindingsSection {
    quit: Option<String>,
    clear: Option<String>,
    help: Option<String>,
    model_picker: Option<String>,
    agent_picker: Option<String>,
    context_manager: Option<String>,
    brainstorm: Option<String>,
    tdd_template: Option<String>,
    git_dashboard: Option<String>,
    goals_dashboard: Option<String>,
    skills_panel: Option<String>,
    metrics_dashboard: Option<String>,
    scroll_mode: Option<String>,
}

#[derive(Deserialize)]
struct DefaultAgentSection {
    name: Option<String>,
    #[serde(rename = "displayName")]
    display_name: Option<String>,
    emoji: Option<String>,
}

impl Config {
    pub fn resolve(url_override: Option<String>, group: String) -> Self {
        let ui = load_ui_prefs();

        // 1. CLI flag override
        if let Some(url) = url_override {
            return Self { ws_url: url, group, ui };
        }

        // 2. Env var override
        if let Ok(url) = std::env::var("AETHER_WS_URL") {
            return Self { ws_url: url, group, ui };
        }

        // 3. ~/.aether/config.json → workspace → .aether.json
        if let Some(port) = try_resolve_from_config_files(&group) {
            return Self {
                ws_url: format!("ws://localhost:{}/ws", port),
                group,
                ui,
            };
        }

        // 4. Fallback
        Self {
            ws_url: "ws://localhost:5099/ws".to_string(),
            group,
            ui,
        }
    }
}

fn try_resolve_from_config_files(group: &str) -> Option<u16> {
    let home = std::env::var("HOME").ok()?;
    let config_path = PathBuf::from(&home).join(".aether/config.json");
    let config_bytes = std::fs::read(&config_path).ok()?;
    let config: AetherConfig = serde_json::from_slice(&config_bytes).ok()?;
    let workspace = config.agents?.get(group)?.workspace.as_ref()?.clone();
    let ws_config_path = PathBuf::from(&workspace).join(".aether.json");
    let ws_bytes = std::fs::read(&ws_config_path).ok()?;
    let ws_config: WorkspaceConfig = serde_json::from_slice(&ws_bytes).ok()?;
    ws_config.websocket.and_then(|w| w.port).or(ws_config.port)
}

fn load_ui_prefs() -> UiPrefs {
    let mut prefs = UiPrefs::default();
    let home = match std::env::var("HOME") {
        Ok(h) => PathBuf::from(h),
        Err(_) => return prefs,
    };
    let config_path = home.join(".aether/config.json");
    let bytes = match std::fs::read(&config_path) {
        Ok(b) => b,
        Err(_) => return prefs,
    };
    let cfg: AetherConfig = match serde_json::from_slice(&bytes) {
        Ok(c) => c,
        Err(_) => return prefs,
    };

    if let Some(ui) = cfg.ui {
        if let Some(phrases) = ui.waiting_phrases {
            if !phrases.is_empty() {
                prefs.waiting_phrases = phrases;
            }
        }
        if let Some(default) = ui.default_waiting_phrase {
            prefs.default_waiting_phrase = default;
        }
        if let Some(theme_name) = ui.theme {
            crate::ui::theme::set_theme(&theme_name);
        }
        if let Some(kb) = ui.keybindings {
            if let Some(k) = kb.quit.and_then(|s| parse_key(&s)) { prefs.keybindings.quit = k; }
            if let Some(k) = kb.clear.and_then(|s| parse_key(&s)) { prefs.keybindings.clear = k; }
            if let Some(k) = kb.help.and_then(|s| parse_key(&s)) { prefs.keybindings.help = k; }
            if let Some(k) = kb.model_picker.and_then(|s| parse_key(&s)) { prefs.keybindings.model_picker = k; }
            if let Some(k) = kb.agent_picker.and_then(|s| parse_key(&s)) { prefs.keybindings.agent_picker = k; }
            if let Some(k) = kb.context_manager.and_then(|s| parse_key(&s)) { prefs.keybindings.context_manager = k; }
            if let Some(k) = kb.brainstorm.and_then(|s| parse_key(&s)) { prefs.keybindings.brainstorm = k; }
            if let Some(k) = kb.tdd_template.and_then(|s| parse_key(&s)) { prefs.keybindings.tdd_template = k; }
            if let Some(k) = kb.git_dashboard.and_then(|s| parse_key(&s)) { prefs.keybindings.git_dashboard = k; }
            if let Some(k) = kb.goals_dashboard.and_then(|s| parse_key(&s)) { prefs.keybindings.goals_dashboard = k; }
            if let Some(k) = kb.skills_panel.and_then(|s| parse_key(&s)) { prefs.keybindings.skills_panel = k; }
            if let Some(k) = kb.metrics_dashboard.and_then(|s| parse_key(&s)) { prefs.keybindings.metrics_dashboard = k; }
            if let Some(k) = kb.scroll_mode.and_then(|s| parse_key(&s)) { prefs.keybindings.scroll_mode = k; }
        }
    }

    if let Some(agent) = cfg.default_agent {
        if let Some(name) = agent.name {
            prefs.default_agent.name = name;
        }
        if let Some(display) = agent.display_name {
            prefs.default_agent.display_name = display;
        }
        if let Some(emoji) = agent.emoji {
            prefs.default_agent.emoji = emoji;
        }
    }

    prefs
}

pub fn load_available_agents() -> Vec<(String, String, String)> {
    let mut result = vec![];
    let defaults = load_ui_prefs().default_agent;
    if let Some(home) = std::env::var("HOME").ok() {
        let config_path = PathBuf::from(&home).join(".aether/config.json");
        if let Ok(config_bytes) = std::fs::read(&config_path) {
            #[derive(Deserialize)]
            struct SimpleConfig {
                agents: Option<std::collections::HashMap<String, RawAgentEntry>>,
            }
            if let Ok(cfg) = serde_json::from_slice::<SimpleConfig>(&config_bytes) {
                if let Some(agents) = cfg.agents {
                    for (name, entry) in agents {
                        if name == "defaults" { continue; }
                        let display = entry.display_name.unwrap_or_else(|| name.clone());
                        let emo = entry.emoji.unwrap_or_else(|| "🤖".to_string());
                        result.push((name, display, emo));
                    }
                }
            }
        }
    }
    if result.is_empty() {
        result.push((defaults.name, defaults.display_name, defaults.emoji));
    }
    result.sort_by(|a, b| a.0.cmp(&b.0));
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_key_single_char() {
        let result = parse_key("q");
        assert_eq!(result, Some((KeyCode::Char('q'), KeyModifiers::NONE)));
    }

    #[test]
    fn test_parse_key_ctrl_combination() {
        let result = parse_key("c+q");
        assert_eq!(result, Some((KeyCode::Char('q'), KeyModifiers::CONTROL)));

        let result = parse_key("ctrl+q");
        assert_eq!(result, Some((KeyCode::Char('q'), KeyModifiers::CONTROL)));
    }

    #[test]
    fn test_parse_key_shift_combination() {
        let result = parse_key("s+a");
        assert_eq!(result, Some((KeyCode::Char('a'), KeyModifiers::SHIFT)));
    }

    #[test]
    fn test_parse_key_alt_combination() {
        let result = parse_key("a+x");
        assert_eq!(result, Some((KeyCode::Char('x'), KeyModifiers::ALT)));
    }

    #[test]
    fn test_parse_key_function_keys() {
        assert_eq!(parse_key("f1"), Some((KeyCode::F(1), KeyModifiers::NONE)));
        assert_eq!(parse_key("f10"), Some((KeyCode::F(10), KeyModifiers::NONE)));
        assert_eq!(parse_key("F5"), Some((KeyCode::F(5), KeyModifiers::NONE)));
    }

    #[test]
    fn test_parse_key_special_keys() {
        assert_eq!(parse_key("esc"), Some((KeyCode::Esc, KeyModifiers::NONE)));
        assert_eq!(parse_key("escape"), Some((KeyCode::Esc, KeyModifiers::NONE)));
        assert_eq!(parse_key("enter"), Some((KeyCode::Enter, KeyModifiers::NONE)));
        assert_eq!(parse_key("tab"), Some((KeyCode::Tab, KeyModifiers::NONE)));
        assert_eq!(parse_key("backspace"), Some((KeyCode::Backspace, KeyModifiers::NONE)));
        assert_eq!(parse_key("delete"), Some((KeyCode::Delete, KeyModifiers::NONE)));
    }

    #[test]
    fn test_parse_key_navigation_keys() {
        assert_eq!(parse_key("up"), Some((KeyCode::Up, KeyModifiers::NONE)));
        assert_eq!(parse_key("down"), Some((KeyCode::Down, KeyModifiers::NONE)));
        assert_eq!(parse_key("left"), Some((KeyCode::Left, KeyModifiers::NONE)));
        assert_eq!(parse_key("right"), Some((KeyCode::Right, KeyModifiers::NONE)));
        assert_eq!(parse_key("home"), Some((KeyCode::Home, KeyModifiers::NONE)));
        assert_eq!(parse_key("end"), Some((KeyCode::End, KeyModifiers::NONE)));
        assert_eq!(parse_key("pageup"), Some((KeyCode::PageUp, KeyModifiers::NONE)));
        assert_eq!(parse_key("pagedown"), Some((KeyCode::PageDown, KeyModifiers::NONE)));
    }

    #[test]
    fn test_parse_key_space() {
        assert_eq!(parse_key("space"), Some((KeyCode::Char(' '), KeyModifiers::NONE)));
    }

    #[test]
    fn test_parse_key_invalid() {
        assert_eq!(parse_key("invalid-key"), None);
        assert_eq!(parse_key("ctrl-invalid"), None);
        assert_eq!(parse_key(""), None);
    }

    #[test]
    fn test_keybindings_default() {
        let kb = KeyBindings::default();
        assert_eq!(kb.quit, (KeyCode::Char('q'), KeyModifiers::CONTROL));
        assert_eq!(kb.clear, (KeyCode::Char('l'), KeyModifiers::CONTROL));
        assert_eq!(kb.help, (KeyCode::F(1), KeyModifiers::NONE));
        assert_eq!(kb.model_picker, (KeyCode::F(2), KeyModifiers::NONE));
        assert_eq!(kb.agent_picker, (KeyCode::F(3), KeyModifiers::NONE));
    }
}
