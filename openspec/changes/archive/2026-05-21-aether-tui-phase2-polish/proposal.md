## Why

Phase 1 delivers a working chat client. Phase 2 adds the polish features that make `aether-tui` feel production-grade: model picker panel (`F2`), conversation history resume on connect, and a proper scroll mode for navigating long conversations. These were explicitly deferred from Phase 1 and are spec'd separately to keep each change atomic.

## What Changes

- **NEW** `clients/aether-tui/src/` — additions to existing Rust crate (no new files at crate level, additions within existing modules):
  - `app.rs` — add `Chat::ModelPicker` state to FSM, model list in `AppState`
  - `ui.rs` — floating model picker panel, scroll mode navigation hints
  - `ws.rs` — send `{"type":"list_models"}` on connect, handle `{"type":"models","providers":[...]}` response; send `{"type":"get_history","group":"maria","limit":50}` on connect, handle `{"type":"history","messages":[...]}` response
  - `commands.rs` — `/models` local command opens picker
  - `events.rs` — `AppEvent::ModelsLoaded`, `AppEvent::HistoryLoaded` variants

## Capabilities

### New Capabilities

- `tui-model-picker`: Floating panel (`F2`/`Ctrl+M`) showing providers + models grouped, `j/k` navigate, `Enter` select, `Esc` close; status bar updates with new model name
- `tui-history-resume`: On WS connect, automatically request last 50 messages and render them in the chat area before accepting user input
- `tui-scroll-mode`: `Esc` toggles scroll mode; `j/k`, `PgUp/PgDn`, `G`/`gg`, mouse wheel navigate history; status bar shows `Scroll` mode indicator

### Modified Capabilities

- `tui-websocket-client`: Add `list_models` request on connect + `models` response handling; add `get_history` request on connect + `history` response handling (delta — new inbound/outbound message types)
- `tui-rust-client`: Add `Chat::ModelPicker` FSM state and scroll mode keyboard bindings (delta — new keyboard behaviours)

## Impact

- No C# backend changes (Phase 2 requires Phase 3 backend handlers — Phase 2 TUI code is written but the features will silently no-op until Phase 3 is deployed; graceful degradation required)
- `clients/aether-tui/` — modifications to existing Rust crate
- No new Cargo dependencies beyond Phase 1 set
