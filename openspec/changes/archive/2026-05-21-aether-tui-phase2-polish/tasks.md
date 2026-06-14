# aether-tui Phase 2 — Implementation Tasks

> **Scope:** Extend the existing `clients/aether-tui/` crate with model picker panel, history resume, and scroll mode.
>
> **Prerequisite:** Phase 1 tasks.md complete and binary builds successfully.
>
> **Note:** Phase 2 TUI code works with graceful degradation when Phase 3 backend not yet deployed (2s timeouts).

---

## Task 1: `events.rs` — Add Phase 2 event variants (already stubbed in Phase 1)

**Files:**
- Modify: `clients/aether-tui/src/events.rs`

- [x] **Step 1.1: Verify `HistoryLoaded` and `ModelsLoaded` variants exist**

  The Phase 1 `events.rs` already includes these stubs. Confirm they compile.
  If not added in Phase 1, add now:
  ```rust
  HistoryLoaded(Vec<Message>),
  ModelsLoaded(Option<ModelsPayload>),
  ```

---

## Task 2: `ws.rs` — Add `list_models` + `get_history` send and response handling

**Files:**
- Modify: `clients/aether-tui/src/ws.rs`

- [x] **Step 2.1: Send `list_models` immediately after connect**

  After `tx.send(AppEvent::Connected)`, immediately queue:
  ```rust
  // Send list_models to populate picker
  let _ = write.send(WsMessage::Text(
      r#"{"type":"list_models"}"#.to_string().into()
  )).await;
  ```

- [x] **Step 2.2: Send `get_history` immediately after connect**

  ```rust
  let history_req = serde_json::json!({
      "type": "get_history",
      "group": group,  // pass group into ws_task as parameter
      "limit": 50
  });
  let _ = write.send(WsMessage::Text(history_req.to_string().into())).await;
  ```
  **Note:** `ws_task` signature must be updated to accept `group: String` parameter.

- [x] **Step 2.3: Add 2-second timeout for history response**

  After sending `get_history`, start a `tokio::time::sleep(Duration::from_secs(2))` in a separate task. If history response not received within 2s, send `AppEvent::HistoryLoaded(vec![])`.

  Pattern:
  ```rust
  // In ws_task, after sending get_history:
  let history_tx = tx.clone();
  tokio::spawn(async move {
      tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
      let _ = history_tx.send(AppEvent::HistoryLoaded(vec![])).await;
  });
  ```
  The `handle_event` for `HistoryLoaded` must be idempotent (second call with empty vec is no-op if already loaded).

- [x] **Step 2.4: Handle `models` response in `handle_inbound`**

  ```rust
  "models" => {
      let payload = parse_models_payload(&val);
      let _ = tx.send(AppEvent::ModelsLoaded(Some(payload))).await;
  }
  ```

  ```rust
  fn parse_models_payload(val: &serde_json::Value) -> ModelsPayload {
      let current = val["current"].as_str().unwrap_or("none").to_string();
      let think_effort = val["think_effort"].as_str().map(str::to_string);
      let providers = val["providers"].as_array().unwrap_or(&vec![]).iter().map(|p| {
          ProviderGroup {
              name: p["name"].as_str().unwrap_or("?").to_string(),
              models: p["models"].as_array().unwrap_or(&vec![]).iter()
                  .filter_map(|m| m.as_str().map(str::to_string))
                  .collect(),
          }
      }).collect();
      ModelsPayload { current, think_effort, providers }
  }
  ```

- [x] **Step 2.5: Handle `history` response in `handle_inbound`**

  ```rust
  "history" => {
      use crate::events::{Message, Role};
      let messages = val["messages"].as_array().unwrap_or(&vec![]).iter().map(|m| {
          let role = match m["role"].as_str().unwrap_or("user") {
              "assistant" => Role::Assistant,
              _ => Role::User,
          };
          let content = m["content"].as_str().unwrap_or("").to_string();
          let timestamp = m["timestamp"].as_str()
              .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
              .map(|dt| dt.with_timezone(&chrono::Utc))
              .unwrap_or_else(chrono::Utc::now);
          Message { role, content, timestamp, is_historical: true }
      }).collect();
      let _ = tx.send(AppEvent::HistoryLoaded(messages)).await;
  }
  ```

---

## Task 3: `app.rs` — Handle Phase 2 events

**Files:**
- Modify: `clients/aether-tui/src/app.rs`

- [x] **Step 3.1: Handle `HistoryLoaded` in `handle_event`**

  ```rust
  AppEvent::HistoryLoaded(msgs) => {
      if !self.history_loaded {
          // Prepend historical messages before any live messages
          let mut combined = msgs;
          combined.append(&mut self.messages);
          self.messages = combined;
          self.history_loaded = true;
      }
      // No-op if already loaded (idempotent)
  }
  ```

  Also change Phase 1 init: `history_loaded: false` (was `true`).

- [x] **Step 3.2: Handle `ModelsLoaded` in `handle_event`**

  ```rust
  AppEvent::ModelsLoaded(payload) => {
      self.models = payload;
  }
  ```

- [x] **Step 3.3: Input bar locked until `history_loaded`**

  In `send_message()`:
  ```rust
  if !self.history_loaded { return None; }
  ```

- [x] **Step 3.4: Model picker state in `AppState`**

  Add `picker_selection: usize` to `AppState` for current cursor position in model picker.

---

## Task 4: Model picker keyboard handling in `main.rs`

**Files:**
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 4.1: `F2`/`Ctrl+M` opens model picker**

  In `Normal` mode key handler:
  ```rust
  KeyCode::F(2) | KeyCode::Char('m') if ctrl => {
      state.mode = AppMode::ModelPicker;
      state.picker_selection = 0;
  }
  ```

- [x] **Step 4.2: Picker navigation (`j`/`k`, `Enter`, `Esc`)**

  In `ModelPicker` mode:
  ```rust
  KeyCode::Char('j') | KeyCode::Down => {
      // Move down, skip provider headers
      let max = total_selectable_models(&state);
      state.picker_selection = (state.picker_selection + 1).min(max.saturating_sub(1));
  }
  KeyCode::Char('k') | KeyCode::Up => {
      state.picker_selection = state.picker_selection.saturating_sub(1);
  }
  KeyCode::Enter => {
      if let Some(model) = selected_model(&state) {
          let json = serde_json::json!({
              "type": "command",
              "text": format!("/model {}", model),
              "group": state.group
          });
          let _ = ws_out_tx.send(json.to_string()).await;
      }
      state.mode = AppMode::Normal;
  }
  KeyCode::Esc => {
      state.mode = AppMode::Normal;
  }
  ```

---

## Task 5: Scroll mode keyboard handling in `main.rs`

**Files:**
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 5.1: `Esc` toggles scroll mode**

  In `Normal` mode:
  ```rust
  KeyCode::Esc => { state.mode = AppMode::Scroll; }
  ```
  In `Scroll` mode:
  ```rust
  KeyCode::Esc => {
      state.mode = AppMode::Normal;
      state.scroll_offset = 0;
  }
  KeyCode::Char(c) if !c.is_control() => {
      state.mode = AppMode::Normal;
      state.scroll_offset = 0;
      state.input.push(c);
  }
  ```

- [x] **Step 5.2: `j`/`k`/`PgUp`/`PgDn`/`G`/`gg` navigation**

  ```rust
  // In Scroll mode:
  KeyCode::Char('j') | KeyCode::Down => {
      state.scroll_offset = state.scroll_offset.saturating_sub(1);
  }
  KeyCode::Char('k') | KeyCode::Up => {
      state.scroll_offset += 1; // clamped in ui.rs render
  }
  KeyCode::PageUp => {
      state.scroll_offset += 10;
  }
  KeyCode::PageDown => {
      state.scroll_offset = state.scroll_offset.saturating_sub(10);
  }
  KeyCode::Char('G') => {
      state.scroll_offset = 0; // jump to bottom
  }
  KeyCode::Char('g') => {
      if last_key_was_g {
          state.scroll_offset = usize::MAX; // ui.rs will clamp to actual max
          last_key_was_g = false;
      } else {
          last_key_was_g = true;
      }
  }
  ```

---

## Task 6: `ui.rs` — Model picker overlay + scroll mode + history rendering

**Files:**
- Modify: `clients/aether-tui/src/ui.rs`

- [x] **Step 6.1: Apply `scroll_offset` to chat area rendering**

  When rendering messages, slice from the bottom:
  ```rust
  let visible_start = total_lines.saturating_sub(chat_height + state.scroll_offset);
  // Render lines[visible_start..visible_start + chat_height]
  ```

- [x] **Step 6.2: Render historical messages with dimmed timestamp prefix**

  ```rust
  if msg.is_historical {
      // Add dim timestamp line before content
      let ts = msg.timestamp.format("%H:%M").to_string();
      // Render ts in DIM colour, then content in normal colour
  }
  ```
  Show `── resumed ──` separator between last historical and first live message.

- [x] **Step 6.3: Model picker overlay**

  ```rust
  if state.mode == AppMode::ModelPicker {
      // Calculate popup area: 50% width, centered, height = min(items+4, 20)
      let popup_area = centered_rect(50, min_height, frame.area());
      frame.render_widget(Clear, popup_area);
      // Draw Block with List inside
      // Mark current model with "← now"
      // Footer: "j/k Navigate · Enter Select · Esc Close"
  }
  ```

- [x] **Step 6.4: Status bar shows scroll position and mode indicator**

  In `Scroll` mode: show `[SCROLL · L{top}-{bottom}/{total}]` in amber.
  In `Normal`: remove scroll indicator.

---

## Task 7: Verify Phase 2 features

- [x] **Step 7.1: Build and test model picker**

  ```bash
  cd clients/aether-tui && cargo build --release
  ```
  Launch TUI → press `F2` → verify picker appears with providers and models.
  Navigate with `j/k` → press `Enter` → verify `/model` command sent and header updates.

- [ ] **Step 7.2: Test history resume (requires Phase 3 backend)**

  If Phase 3 deployed: connect TUI → verify historical messages appear before input unlocked.
  If Phase 3 NOT deployed: connect TUI → verify 2s wait → input unlocks → no crash.

- [ ] **Step 7.3: Test scroll mode**

  Send 30+ messages → press `Esc` → verify `[SCROLL]` indicator.
  Press `k` → verify scrolling up. Press `G` → verify jump to bottom.
  Press `Esc` → verify return to `Normal` with offset reset.
