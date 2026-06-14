## Why

The existing C# `Aether.Tui` (Terminal.Gui) has a broken reflection-based callback wiring: `OnMessageReceived` does not exist, causing the chat window to remain blank when Maria replies. A replacement native terminal client is needed. Rust + Ratatui + tokio-tungstenite provides a single self-contained binary with no .NET runtime dependency, direct async WebSocket streaming, and full control over the Athanor Fire theme.

## What Changes

- **NEW** `clients/aether-tui/` — Rust workspace crate, single binary output `aether-tui`
- **NEW** `clients/aether-tui/src/config.rs` — reads `~/.aether/config.json` → workspace `.aether.json` → fallback `localhost:5099`
- **NEW** `clients/aether-tui/src/events.rs` — `AppEvent` enum merging terminal key events + WebSocket messages
- **NEW** `clients/aether-tui/src/ws.rs` — async WebSocket client with auto-reconnect (exponential backoff), streaming token buffering
- **NEW** `clients/aether-tui/src/app.rs` — `AppState` + finite state machine (`Connecting → Chat::Normal ↔ Chat::Scroll ↔ Chat::ModelPicker`)
- **NEW** `clients/aether-tui/src/ui.rs` — Ratatui draw calls with Athanor Fire colour theme
- **NEW** `clients/aether-tui/src/commands.rs` — slash command dispatcher: LOCAL (clear/help/quit) vs FORWARDED (model/think/reset/…)
- **NEW** `clients/aether-tui/src/main.rs` — `clap` CLI args, config load, tokio runtime, startup sequence

## Capabilities

### New Capabilities

- `tui-rust-client`: Single-binary Rust TUI for chatting with Aether over WebSocket — chat layout, streaming display, slash command dispatch, Athanor Fire theme, keyboard navigation
- `tui-config-loader`: Config resolution chain `~/.aether/config.json` → workspace `.aether.json` → env var `AETHER_WS_URL` → localhost fallback
- `tui-websocket-client`: Async WS connection with auto-reconnect, token-level streaming rendering, `AppEvent` multiplexed channel

### Modified Capabilities

*(none — new crate, no existing specs affected)*

## Impact

- New `clients/aether-tui/` directory inside the existing Aether mono-repo
- `Cargo.toml` at crate root (not workspace-level — Aether is a .NET solution)
- No changes to `src/` C# code in Phase 1
- Runtime dependency: Aether backend must be running and WS channel enabled (`channels:websocket:enabled: true`, default port 5099)
