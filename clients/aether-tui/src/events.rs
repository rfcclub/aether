use chrono::{DateTime, Utc};

#[derive(Debug, Clone)]
pub enum Role {
    User,
    Assistant,
}

#[derive(Debug, Clone)]
pub struct Message {
    pub role: Role,
    pub content: String,
    pub timestamp: DateTime<Utc>,
    pub is_historical: bool,
}

#[derive(Debug)]
pub enum AppEvent {
    /// WS connection established
    Connected,
    /// WS disconnected (reason string)
    Disconnected(String),
    /// Streaming token chunk received
    StreamChunk(String),
    /// Complete message received (non-streaming or stream end)
    MessageComplete(String),
    /// Typing indicator state
    Typing(bool),
    /// Error message from backend
    BackendError(String),
    /// History loaded (Phase 2 — populated by ws.rs after get_history)
    HistoryLoaded(Vec<Message>),
    /// Models payload received (Phase 2)
    ModelsLoaded(Option<ModelsPayload>),
    /// Git status files loaded
    GitStatusLoaded(Vec<(String, String)>),
    /// Git diff content loaded
    GitDiffLoaded(String),
    /// Goals dashboard data loaded
    GoalsLoaded(serde_json::Value),
    /// Skills panel data loaded
    SkillsLoaded(serde_json::Value),
    /// Self-improvement metrics data loaded
    MetricsLoaded(serde_json::Value),
    /// Telemetry snapshot loaded
    TelemetryLoaded(serde_json::Value),
    /// Quit signal
    Quit,
}

#[derive(Debug, Clone)]
pub struct ModelsPayload {
    pub current: String,
    pub think_effort: Option<String>,
    pub providers: Vec<ProviderGroup>,
}

#[derive(Debug, Clone)]
pub struct ProviderGroup {
    pub name: String,
    pub models: Vec<String>,
}
