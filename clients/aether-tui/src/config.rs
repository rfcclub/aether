use serde::Deserialize;
use std::path::PathBuf;

#[derive(Debug, Clone)]
pub struct Config {
    pub ws_url: String,
    pub group: String,
}

#[derive(Deserialize, Default)]
struct AetherConfig {
    agents: Option<std::collections::HashMap<String, AgentEntry>>,
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

impl Config {
    pub fn resolve(url_override: Option<String>, group: String) -> Self {
        // 1. CLI flag override
        if let Some(url) = url_override {
            return Self { ws_url: url, group };
        }

        // 2. Env var override
        if let Ok(url) = std::env::var("AETHER_WS_URL") {
            return Self { ws_url: url, group };
        }

        // 3. ~/.aether/config.json → workspace → .aether.json
        if let Some(port) = try_resolve_from_config_files(&group) {
            return Self {
                ws_url: format!("ws://localhost:{}/ws", port),
                group,
            };
        }

        // 4. Fallback
        Self {
            ws_url: "ws://localhost:5099/ws".to_string(),
            group,
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

pub fn load_available_agents() -> Vec<(String, String, String)> {
    let mut result = vec![];
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
        result.push(("maria".to_string(), "Maria".to_string(), "🌸".to_string()));
    }
    result.sort_by(|a, b| a.0.cmp(&b.0));
    result
}
