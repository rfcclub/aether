# aether-tui Phase 1 — Implementation Tasks

> **Scope:** Build the `clients/aether-tui/` Rust crate from scratch. Single binary, Ratatui chat UI, WebSocket client, Athanor Fire theme, slash command dispatch.
>
> **Tech stack:** Rust stable, Ratatui 0.29, crossterm 0.28, tokio 1 (full), tokio-tungstenite 0.26, serde/serde_json 1, clap 4 (derive), chrono 0.4
>
> **Backend:** Aether WebSocket channel on port 5099 must be running (`channels:websocket:enabled: true`)

---

## Task 1: Cargo project scaffold

**Files:**
- Create: `clients/aether-tui/Cargo.toml`
- Create: `clients/aether-tui/src/main.rs` (stub)

- [x] **Step 1.1: Create `clients/aether-tui/Cargo.toml`**

  ```toml
  [package]
  name = "aether-tui"
  version = "0.1.0"
  edition = "2021"
  description = "Terminal UI client for Aether AI — Athanor Fire theme"

  [[bin]]
  name = "aether-tui"
  path = "src/main.rs"

  [dependencies]
  ratatui           = "0.29"
  crossterm         = "0.28"
  tokio             = { version = "1", features = ["full"] }
  tokio-tungstenite = { version = "0.26", features = ["native-tls"] }
  serde             = { version = "1", features = ["derive"] }
  serde_json        = "1"
  clap              = { version = "4", features = ["derive"] }
  chrono            = { version = "0.4", features = ["serde"] }
  futures-util      = "0.3"
  ```

- [x] **Step 1.2: Verify build compiles (stub `main.rs`)**

  Create `clients/aether-tui/src/main.rs`:
  ```rust
  fn main() {
      println!("aether-tui stub");
  }
  ```
  Run: `cd clients/aether-tui && cargo check`
  Expected: no errors.

---

## Task 2: `config.rs` — Config resolution chain

**Files:**
- Create: `clients/aether-tui/src/config.rs`

- [x] **Step 2.1: Define `Config` struct and resolution logic**

  ```rust
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
  ```

---

## Task 3: `events.rs` — AppEvent enum

**Files:**
- Create: `clients/aether-tui/src/events.rs`

- [x] **Step 3.1: Define `AppEvent` and `Message` types**

  ```rust
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
  ```

---

## Task 4: `ws.rs` — WebSocket client with auto-reconnect

**Files:**
- Create: `clients/aether-tui/src/ws.rs`

- [x] **Step 4.1: Write WS task with reconnect loop**

  ```rust
  use crate::events::AppEvent;
  use futures_util::{SinkExt, StreamExt};
  use tokio::sync::mpsc;
  use tokio_tungstenite::{connect_async, tungstenite::Message as WsMessage};

  pub async fn ws_task(url: String, tx: mpsc::Sender<AppEvent>, mut rx: mpsc::Receiver<String>) {
      let mut attempt = 0u32;
      loop {
          match connect_async(&url).await {
              Ok((ws_stream, _)) => {
                  attempt = 0;
                  let _ = tx.send(AppEvent::Connected).await;
                  let (mut write, mut read) = ws_stream.split();

                  loop {
                      tokio::select! {
                          // Outbound: messages from app to send
                          msg = rx.recv() => {
                              match msg {
                                  Some(json) => {
                                      if write.send(WsMessage::Text(json.into())).await.is_err() {
                                          break;
                                      }
                                  }
                                  None => return, // channel closed = quit
                              }
                          }
                          // Inbound: messages from server
                          item = read.next() => {
                              match item {
                                  Some(Ok(WsMessage::Text(text))) => {
                                      handle_inbound(text.as_str(), &tx).await;
                                  }
                                  Some(Ok(WsMessage::Close(_))) | None => break,
                                  Some(Err(_)) => break,
                                  _ => {}
                              }
                          }
                      }
                  }
                  let _ = tx.send(AppEvent::Disconnected("connection closed".into())).await;
              }
              Err(e) => {
                  let _ = tx.send(AppEvent::Disconnected(e.to_string())).await;
              }
          }

          // Exponential backoff: 1, 2, 4, 8, … capped at 30 seconds
          let wait = std::cmp::min(1u64 << attempt, 30);
          attempt = attempt.saturating_add(1);
          tokio::time::sleep(tokio::time::Duration::from_secs(wait)).await;
      }
  }

  async fn handle_inbound(json: &str, tx: &mpsc::Sender<AppEvent>) {
      let Ok(val) = serde_json::from_str::<serde_json::Value>(json) else { return };
      let msg_type = val["type"].as_str().unwrap_or("");
      match msg_type {
          "chunk" | "streaming_chunk" => {
              if let Some(text) = val["text"].as_str() {
                  let _ = tx.send(AppEvent::StreamChunk(text.to_string())).await;
              }
          }
          "message" => {
              if let Some(text) = val["text"].as_str() {
                  let _ = tx.send(AppEvent::MessageComplete(text.to_string())).await;
              }
          }
          "typing" => {
              let is_typing = val["is_typing"].as_bool().unwrap_or(false);
              let _ = tx.send(AppEvent::Typing(is_typing)).await;
          }
          "error" => {
              let msg = val["message"].as_str()
                  .or_else(|| val["text"].as_str())
                  .unwrap_or("unknown error");
              let _ = tx.send(AppEvent::BackendError(msg.to_string())).await;
          }
          _ => {} // unknown types silently ignored
      }
  }
  ```

- [x] **Step 4.2: Verify `cargo check` passes with `ws.rs` added**

---

## Task 5: `commands.rs` — Slash command dispatch

**Files:**
- Create: `clients/aether-tui/src/commands.rs`

- [x] **Step 5.1: Write dispatch logic**

  ```rust
  /// Commands handled locally by the TUI — never forwarded to server
  const LOCAL_COMMANDS: &[&str] = &["/clear", "/help", "/quit", "/q"];

  #[derive(Debug, Clone, PartialEq)]
  pub enum CommandAction {
      Local(LocalCommand),
      Forward(String), // JSON string to send
  }

  #[derive(Debug, Clone, PartialEq)]
  pub enum LocalCommand {
      Clear,
      Help,
      Quit,
  }

  pub fn dispatch(input: &str, group: &str) -> Option<CommandAction> {
      if !input.starts_with('/') {
          return None; // not a slash command
      }
      let cmd_lower = input.split_whitespace().next().unwrap_or("").to_lowercase();
      match cmd_lower.as_str() {
          "/clear" => Some(CommandAction::Local(LocalCommand::Clear)),
          "/help"  => Some(CommandAction::Local(LocalCommand::Help)),
          "/quit" | "/q" => Some(CommandAction::Local(LocalCommand::Quit)),
          _ => {
              // Forward to server
              let json = serde_json::json!({
                  "type": "command",
                  "text": input,
                  "group": group
              });
              Some(CommandAction::Forward(json.to_string()))
          }
      }
  }
  ```

---

## Task 6: `app.rs` — AppState and FSM

**Files:**
- Create: `clients/aether-tui/src/app.rs`

- [x] **Step 6.1: Define `AppMode`, `AppState`, event handler**

  ```rust
  use crate::events::{AppEvent, Message, ModelsPayload, Role};
  use chrono::Utc;

  #[derive(Debug, Clone, PartialEq)]
  pub enum AppMode {
      Connecting,
      Normal,
      Scroll,
      ModelPicker,  // Phase 2 — stub in Phase 1
      ShowHelp,
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
      // Phase 2 fields (stub — always None in Phase 1)
      pub models: Option<ModelsPayload>,
      pub scroll_offset: usize,
      pub history_loaded: bool,
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
              history_loaded: true, // Phase 1: skip history loading
          }
      }

      pub fn handle_event(&mut self, event: AppEvent) -> bool {
          // Returns true if app should quit
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
              }
              AppEvent::MessageComplete(text) => {
                  if !self.streaming_buf.is_empty() {
                      // Streaming mode: finalise buffer
                      self.messages.push(Message {
                          role: Role::Assistant,
                          content: std::mem::take(&mut self.streaming_buf),
                          timestamp: Utc::now(),
                          is_historical: false,
                      });
                  } else {
                      // Non-streaming complete message
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
              // Phase 2 stubs — no-op in Phase 1
              AppEvent::HistoryLoaded(_) | AppEvent::ModelsLoaded(_) => {}
          }
          false
      }

      pub fn send_message(&mut self) -> Option<String> {
          let text = self.input.trim().to_string();
          if text.is_empty() { return None; }
          self.input.clear();
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
  }
  ```

---

## Task 7: `ui.rs` — Ratatui layout with Athanor Fire theme

**Files:**
- Create: `clients/aether-tui/src/ui.rs`

- [x] **Step 7.1: Define theme constants**

  ```rust
  use ratatui::style::Color;

  pub const BG:           Color = Color::Rgb(13,  13,  13);
  pub const USER_NAME:    Color = Color::Rgb(91,  200, 245);
  pub const USER_TEXT:    Color = Color::Rgb(200, 200, 200);
  pub const AGENT_NAME:   Color = Color::Rgb(255, 179, 71);
  pub const AGENT_TEXT:   Color = Color::Rgb(240, 230, 204);
  pub const CURSOR_COL:   Color = Color::Rgb(255, 107, 0);
  pub const BORDER_FOCUS: Color = Color::Rgb(255, 140, 0);
  pub const CONNECTED:    Color = Color::Rgb(68,  255, 136);
  pub const DISCONNECTED: Color = Color::Rgb(128, 128, 128);
  pub const ERROR_COL:    Color = Color::Rgb(255, 80,  80);
  pub const DIM:          Color = Color::Rgb(100, 100, 100);
  ```

- [x] **Step 7.2: Write `draw()` function**

  The draw function splits the terminal into four areas using `Layout::vertical`:
  - **Header** (1 line): `─ Aether · Maria ───── <model> · Think:<effort> ─`
  - **Chat area** (fill): scrollable `Paragraph` or custom line renderer with message list
  - **Separator** (1 line): thin `─────` border line
  - **Input bar** (3 lines): `Block` with `Paragraph` for `› <input>`
  - **Status bar** (1 line): `● Maria · Normal ── [F1] Help [Ctrl+Q] Quit ──`

  Message rendering rules:
  - User message: `<USER_NAME>Thoor</USER_NAME>` line, then `<USER_TEXT>  ╰─ <content></USER_TEXT>`
  - Agent message: `<AGENT_NAME>Maria</AGENT_NAME>` line, then `<AGENT_TEXT>  ╰─ <content></AGENT_TEXT>`
  - Streaming live line: append `<CURSOR_COL>▋</CURSOR_COL>` to streaming_buf content
  - Error message: render content with `<ERROR_COL>` colour
  - Connected dot: `●` in `CONNECTED` colour when connected, `○` in `DISCONNECTED` when not

- [x] **Step 7.3: Implement help popup overlay (for `/help` and `F1`)**

  Render a centered `Clear` + `Block` with static keybinding table when `AppMode::ShowHelp`.

---

## Task 8: `main.rs` — CLI args, startup, event loop

**Files:**
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 8.1: Write CLI args with clap**

  ```rust
  use clap::Parser;

  #[derive(Parser)]
  #[command(name = "aether-tui", about = "Terminal UI for Aether AI")]
  struct Args {
      /// WebSocket URL override (e.g. ws://localhost:5099/ws)
      #[arg(long)]
      url: Option<String>,

      /// Agent group to connect to
      #[arg(long, default_value = "maria")]
      group: String,
  }
  ```

- [x] **Step 8.2: Write `main()` — setup terminal, spawn tasks, run event loop**

  ```rust
  #[tokio::main]
  async fn main() -> anyhow::Result<()> {
      let args = Args::parse();
      let config = Config::resolve(args.url, args.group);

      // Setup terminal
      crossterm::terminal::enable_raw_mode()?;
      let mut stdout = std::io::stdout();
      crossterm::execute!(stdout, crossterm::terminal::EnterAlternateScreen, crossterm::event::EnableMouseCapture)?;

      // Panic hook: always restore terminal
      let default_hook = std::panic::take_hook();
      std::panic::set_hook(Box::new(move |info| {
          let _ = crossterm::terminal::disable_raw_mode();
          let _ = crossterm::execute!(std::io::stdout(), crossterm::terminal::LeaveAlternateScreen, crossterm::event::DisableMouseCapture);
          default_hook(info);
      }));

      let backend = CrosstermBackend::new(stdout);
      let mut terminal = Terminal::new(backend)?;

      // Channels
      let (app_tx, mut app_rx) = mpsc::channel::<AppEvent>(256);
      let (ws_out_tx, ws_out_rx) = mpsc::channel::<String>(64);

      // Spawn WS task
      let ws_url = config.ws_url.clone();
      let ws_tx = app_tx.clone();
      tokio::spawn(ws_task(ws_url, ws_tx, ws_out_rx));

      let mut state = AppState::new(config.group.clone());
      let mut last_key_was_g = false;

      loop {
          // Render
          terminal.draw(|f| ui::draw(f, &state))?;

          // Event handling with 16ms timeout (≈60fps)
          let timeout = tokio::time::Duration::from_millis(16);
          tokio::select! {
              // App events (WS messages)
              Some(event) = app_rx.recv() => {
                  if state.handle_event(event) { break; }
              }
              // Terminal events
              _ = tokio::time::sleep(timeout) => {
                  if crossterm::event::poll(std::time::Duration::ZERO)? {
                      if let crossterm::event::Event::Key(key) = crossterm::event::read()? {
                          handle_key(&mut state, key, &ws_out_tx, &app_tx, &mut last_key_was_g).await;
                          if state.mode == AppMode::Connecting { /* keep going */ }
                      }
                      if let crossterm::event::Event::Mouse(mouse) = crossterm::event::read()? {
                          handle_mouse(&mut state, mouse);
                      }
                  }
              }
          }
      }

      // Restore terminal
      crossterm::terminal::disable_raw_mode()?;
      crossterm::execute!(terminal.backend_mut(), crossterm::terminal::LeaveAlternateScreen, crossterm::event::DisableMouseCapture)?;
      terminal.show_cursor()?;
      Ok(())
  }
  ```

- [x] **Step 8.3: Write `handle_key()` and `handle_mouse()`**

  Key handling in `Normal` mode:
  - `Enter` → `state.send_message()` → send JSON via `ws_out_tx`
  - `Ctrl+Q` → send `AppEvent::Quit`
  - `Ctrl+L` → `state.messages.clear()`
  - `Esc` → `state.mode = AppMode::Scroll`
  - `F1` / `Char('?')` → `state.mode = AppMode::ShowHelp`
  - `F2` / `Ctrl+M` → no-op in Phase 1 (Phase 2 stub)
  - `Char(c)` → `state.input.push(c)`
  - `Backspace` → `state.input.pop()`

  Key handling in `ShowHelp` mode:
  - Any key → `state.mode = AppMode::Normal`

  Mouse handling:
  - `ScrollUp` → `state.scroll_offset += 3`
  - `ScrollDown` → `state.scroll_offset = state.scroll_offset.saturating_sub(3)`

---

## Task 9: Wire all modules in `main.rs`

- [x] **Step 9.1: Add module declarations at top of `main.rs`**

  ```rust
  mod config;
  mod events;
  mod ws;
  mod app;
  mod ui;
  mod commands;

  use config::Config;
  use events::AppEvent;
  use ws::ws_task;
  use app::{AppMode, AppState};
  ```

- [x] **Step 9.2: Run `cargo build --release` and verify 0 errors**

  ```bash
  cd clients/aether-tui && cargo build --release 2>&1
  ```

---

## Task 10: Smoke test against live Aether backend

- [ ] **Step 10.1: Start Aether backend**

  ```bash
  cd /home/thoor/repo/aether && dotnet run --project src/Aether 2>&1 | head -20
  ```
  Verify: `WebSocket channel listening on port 5099`

- [ ] **Step 10.2: Run aether-tui**

  ```bash
  ./clients/aether-tui/target/release/aether-tui
  ```
  Verify:
  - TUI renders with Athanor Fire theme (dark background, fire orange borders)
  - Status bar shows green `●` connected indicator
  - Type a message → Enter → user message appears in ice blue
  - Maria responds → tokens stream in amber/cream with `▋` cursor
  - `/clear` clears display without sending to server
  - `/model` forwarded — server responds with model change confirmation
  - `Ctrl+Q` exits cleanly, terminal restored

- [ ] **Step 10.3: Verify WS chunk type**

  Use `wscat -c ws://localhost:5099/ws` and send `{"type":"message","text":"hello","group":"maria"}`.
  Observe: check if streaming uses `chunk` or `streaming_chunk` type.
  Update `ws.rs` `handle_inbound` match arm if needed.
