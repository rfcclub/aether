## Context

Aether has a working WebSocket channel (`WebSocketChannel.cs`) on port 5099 with a JSON protocol (`{"type":"message","text":"..."}`, typing indicators, streaming chunks). The existing C# `Aether.Tui` (Terminal.Gui) is broken — its callback wiring uses reflection with property names that do not exist, leaving the chat window blank.

This design specifies the Rust replacement client `aether-tui` — a single-binary terminal UI built on Ratatui + tokio-tungstenite, living at `clients/aether-tui/` within the Aether mono-repo.

Constraints:
- Must connect to existing WS protocol without backend changes in Phase 1
- Must read existing config files (`~/.aether/config.json`, workspace `.aether.json`) without creating new config formats
- Phase 1 scope: chat only — send, receive streaming, slash commands LOCAL + FORWARDED

## Goals / Non-Goals

**Goals:**
- Replace C# TUI with a working Rust binary that renders streaming Maria responses correctly
- Implement Athanor Fire colour theme (locked in handover)
- Support full Phase 1 keyboard map: Enter send, Ctrl+Q quit, Ctrl+L clear, Esc scroll toggle
- Slash command dispatch: LOCAL (clear/help/quit) never sent to server; FORWARDED (model/think/reset/…) sent as `{"type":"command","text":"...","group":"maria"}`
- Config chain: `~/.aether/config.json` → workspace `.aether.json` → `AETHER_WS_URL` env → `ws://localhost:5099/ws`
- Auto-reconnect on disconnect (exponential backoff: 1s → 2s → 4s … cap 30s)
- Streaming: render tokens as they arrive, show blinking cursor `▋` during generation

**Non-Goals:**
- Model picker panel (Phase 2)
- History resume via `get_history` (Phase 2)
- Scroll mode with `j/k` (Phase 2)
- Backend `list_models` / `get_history` / `command` WS handlers (Phase 3)
- Multi-agent support (beyond Phase 1 scope)
- Windows support (Linux/macOS only)

## Decisions

### D1: Crate structure — single binary, flat `src/`

**Decision:** 7 source files in `src/` (`main.rs`, `app.rs`, `ws.rs`, `ui.rs`, `events.rs`, `commands.rs`, `config.rs`) — no sub-modules.

**Rationale:** At Phase 1 scope (~600-800 LOC), sub-modules add indirection without benefit. Files map 1:1 to concerns already named in the handover design.

**Alternative considered:** `lib.rs` + `bin/main.rs` for testability — deferred to Phase 2 when we have enough behaviour to test.

---

### D2: Event loop — `tokio::select!` on two channels

**Decision:** Main async task runs `tokio::select!` over:
1. `mpsc::Receiver<AppEvent>` — WS messages pushed by a background task
2. `crossterm::EventStream` — terminal key/mouse events

**Rationale:** Decouples WS I/O from rendering. WS task owns reconnect logic; main loop owns render + state. No shared `Arc<Mutex<>>` around message buffer — single ownership via channel.

**Alternative considered:** `tokio::sync::watch` for WS state — rejected because multiple message types need full queue, not latest-value semantics.

---

### D3: Streaming rendering — token buffer in `AppState`

**Decision:** `AppState.streaming_buf: String` accumulates tokens from `{"type":"chunk","text":"..."}` messages. On `{"type":"message_complete"}`, the buffer is flushed into `messages` vec. The UI renders `streaming_buf` as a live last line with `▋` suffix.

**Rationale:** Simple, no allocation on every token, no flickering from full redraws (Ratatui double-buffers).

---

### D4: Config resolution — no new format, no writes

**Decision:** Config is read-only. Resolution order:
1. `~/.aether/config.json` → find `agents.maria.workspace`
2. `{workspace}/.aether.json` → find `websocket.port` or `port`
3. Env var `AETHER_WS_URL` (full URL override)
4. Fallback: `ws://localhost:5099/ws`

**Rationale:** Matches what the handover specifies. No new config file — avoids fragmentation.

---

### D5: Athanor Fire theme — hardcoded constants, no theme file

**Decision:** Colours defined as `const` in `ui.rs`:

```rust
const BG:           Color = Color::Rgb(13, 13, 13);      // #0D0D0D
const USER_NAME:    Color = Color::Rgb(91, 200, 245);    // #5BC8F5
const USER_TEXT:    Color = Color::Rgb(200, 200, 200);   // #C8C8C8
const AGENT_NAME:   Color = Color::Rgb(255, 179, 71);    // #FFB347
const AGENT_TEXT:   Color = Color::Rgb(240, 230, 204);   // #F0E6CC
const CURSOR:       Color = Color::Rgb(255, 107, 0);     // #FF6B00
const BORDER_FOCUS: Color = Color::Rgb(255, 140, 0);     // #FF8C00
const CONNECTED:    Color = Color::Rgb(68, 255, 136);    // #44FF88
```

**Rationale:** No user-facing theme switching in Phase 1. Hardcoded constants = zero overhead, easy to read.

---

### D6: Slash command dispatch — local table lookup first

**Decision:** `commands.rs` holds two `const` arrays:
- `LOCAL_COMMANDS: &[&str]` = `["/clear", "/help", "/quit", "/q"]`
- On input: if prefix matches LOCAL → handle locally, do not send to WS
- Otherwise if starts with `/` → wrap in `{"type":"command","text":"...","group":"maria"}` and send

**Rationale:** Clean separation, no regex needed, O(n) with tiny n.

## Risks / Trade-offs

| Risk | Mitigation |
|------|-----------|
| Ratatui version 0.29 API changes from handover deps | Pin exact versions in `Cargo.toml`, verify with `cargo check` immediately |
| WS protocol mismatch (chunk vs streaming_chunk type name) | Inspect live WS traffic with `wscat` before wiring chunk handler; handle both `chunk` and `streaming_chunk` type strings |
| Terminal resize events causing layout panic | Wrap all draw calls in `terminal.autoresize()` + catch `ResizeEvent` as a no-op redraw |
| Config file absent (fresh install) | Fallback to `ws://localhost:5099/ws` — always safe, logged to status bar |
| `crossterm` + `tokio` interaction on Linux with `TERM=xterm-256color` | Test in both kitty and standard xterm; use `crossterm::terminal::enable_raw_mode()` with cleanup on panic via `std::panic::set_hook` |

## Migration Plan

1. Build `aether-tui` binary: `cd clients/aether-tui && cargo build --release`
2. Copy or symlink `target/release/aether-tui` to `~/.local/bin/`
3. Existing `Aether.Tui` C# project remains untouched — no removal in Phase 1
4. Rollback: use existing Telegram channel or run `Aether.Tui` (broken but bootable)

## Open Questions

- Does the existing WS protocol send `{"type":"chunk","text":"..."}` or `{"type":"streaming_chunk",...}`? → verify by running backend + `wscat`, update `ws.rs` constants accordingly (do before first cargo build)
- Should `group` be configurable per CLI arg (`--group`) or always `"maria"` in Phase 1? → handover says "Maria only (tạm thời)" so hardcode `"maria"` with a `--group` flag for override
