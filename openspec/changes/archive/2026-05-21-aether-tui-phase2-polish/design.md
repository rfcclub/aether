## Context

Phase 1 delivers a working chat loop. Phase 2 extends the `aether-tui` Rust crate (same `clients/aether-tui/`) with three UX-complete features: model picker floating panel, conversation history resume, and scroll mode for navigating long threads.

These features depend on Phase 3 backend WS handlers (`list_models`, `get_history`). Phase 2 code SHALL be written with graceful degradation: if the backend does not respond to `list_models` or `get_history` within 2 seconds, the TUI proceeds without crashing.

## Goals / Non-Goals

**Goals:**
- Model picker panel at `F2`/`Ctrl+M` — floating overlay, grouped by provider, keyboard navigable
- History resume — on connect, auto-fetch last 50 messages and render before accepting input
- Scroll mode — `Esc` toggles, `j/k`/`PgUp/PgDn`/`G` navigate, mouse wheel works, `Normal` restores on any typing key

**Non-Goals:**
- Agent switching (always Maria in Phase 2)
- Configurable history limit (hardcoded 50 in Phase 2)
- Multi-window / split pane layout
- Persistent scroll position across reconnects

## Decisions

### D1: Model picker — floating Ratatui `Clear` + `Block` overlay

**Decision:** Render the picker as a centered `Popup` using `ratatui::widgets::Clear` to erase the background, then draw a `Block` with `List` widget inside. The overlay is drawn last in `ui.rs`, covering the chat area.

**Rationale:** Ratatui has no native modal/overlay widget — `Clear` + overdraw is the idiomatic pattern. Simple, no extra crates needed.

**Geometry:** Width = 50% of terminal width (min 40 cols), height = min(number_of_items + 4, 20 rows). Centered.

---

### D2: Model picker data — `AppState.models: Option<ModelsPayload>`

**Decision:** On `AppEvent::ModelsLoaded(payload)`, store the full `models` response in `AppState`. The picker renders from this. If `None` (not yet received), picker shows `Loading…`.

**Rationale:** No extra Arc/Mutex — single ownership via event channel, same pattern as Phase 1.

---

### D3: History resume — send `get_history` immediately after `AppEvent::Connected`

**Decision:** In `app.rs`, on receiving `AppEvent::Connected`, immediately send `{"type":"get_history","group":"maria","limit":50}` over WS. On `AppEvent::HistoryLoaded(messages)`, prepend messages to `AppState.messages` and mark `history_loaded: true`. If no response in 2s, set `history_loaded: true` anyway (timeout via `tokio::time::sleep` in WS task).

**Rationale:** Keeps startup latency low. If Phase 3 not yet deployed, 2s timeout ensures Phase 2 TUI is still usable.

---

### D4: Scroll mode — offset integer in `AppState`

**Decision:** `AppState.scroll_offset: usize` tracks how many lines from the bottom are scrolled up. In `Chat::Scroll` mode, `j/k` increment/decrement offset. `ui.rs` slices the message list using offset. Entering `Normal` mode resets offset to 0 (auto-scroll to bottom).

**Rationale:** Simple, no external scroll state crate. Works correctly even as new messages arrive (offset stays relative to bottom).

## Risks / Trade-offs

| Risk | Mitigation |
|------|-----------|
| Phase 3 not deployed — `list_models` never responds | 2s timeout in WS task → `AppEvent::ModelsLoaded(None)` → picker shows `Loading…` |
| Phase 3 not deployed — `get_history` never responds | 2s timeout → `history_loaded: true` with empty prepend → chat starts fresh, no crash |
| Model picker overlay flicker on slow terminals | Ratatui double-buffer minimises flicker; no additional mitigation needed |
| Scroll offset going negative on `j` at bottom | Clamp: `scroll_offset = scroll_offset.saturating_sub(1)` |
