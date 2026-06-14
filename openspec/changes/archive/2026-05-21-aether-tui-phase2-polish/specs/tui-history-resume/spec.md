# tui-history-resume — Specification

> Capability: Automatic conversation history load on WebSocket connect.

---

## ADDED Requirements

### Requirement: History requested automatically on connect

The system SHALL send a `get_history` request immediately after `AppEvent::Connected` is received, before the user can type.

#### Scenario: History request sent on connect

- **WHEN** WebSocket connection is established
- **THEN** the TUI SHALL send:
  ```json
  {"type":"get_history","group":"maria","limit":50}
  ```
- **THEN** the input bar SHALL be locked (non-interactive) until history is loaded or timeout occurs

#### Scenario: History response received

- **WHEN** the backend responds with `{"type":"history","messages":[...]}`
- **THEN** the messages SHALL be prepended to `AppState.messages` in chronological order (oldest first)
- **THEN** the chat area SHALL scroll to the bottom
- **THEN** the input bar SHALL be unlocked
- **THEN** a subtle separator line SHALL be shown between historical messages and new messages (e.g. `── resumed ──`)

#### Scenario: No history (new conversation)

- **WHEN** the backend responds with `{"type":"history","messages":[]}` (empty)
- **THEN** the chat area SHALL be empty (no separator shown)
- **THEN** the input bar SHALL be unlocked immediately

---

### Requirement: Timeout prevents indefinite wait

The system SHALL unblock the input bar after 2 seconds if no `history` response is received.

#### Scenario: Phase 3 not deployed — no history response

- **WHEN** the TUI sends `get_history` and receives no response within 2 seconds
- **THEN** the system SHALL set `history_loaded: true` and unlock the input bar
- **THEN** the chat area SHALL be empty (no crash, no error message)
- **THEN** a status bar hint SHALL show `[history unavailable]` for 3 seconds then clear

---

### Requirement: History messages rendered with historical style

Historical messages (from `get_history`) SHALL be visually distinguishable from new messages sent in the current session.

#### Scenario: Historical message rendering

- **WHEN** a historical message is rendered
- **THEN** the timestamp SHALL be shown left-aligned in a dimmed style
- **THEN** the message content style SHALL match normal user/agent styling but at reduced brightness (`#888888` for timestamps)
