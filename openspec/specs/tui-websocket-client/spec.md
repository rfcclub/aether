# tui-websocket-client — Specification

> Capability: Async WebSocket client with auto-reconnect and streaming token rendering.

---

## ADDED Requirements

### Requirement: WebSocket connection lifecycle managed automatically

The system SHALL maintain a persistent WebSocket connection to Aether's `/ws` endpoint, automatically reconnecting on disconnection.

#### Scenario: Initial connection established

- **WHEN** the resolved URL is available at startup
- **THEN** the WS task SHALL perform the HTTP upgrade handshake
- **THEN** on success, `AppEvent::Connected` SHALL be sent to the main event channel
- **THEN** the status bar SHALL update to show green `●`

#### Scenario: Connection lost, auto-reconnect

- **WHEN** the WebSocket connection drops (network error, server restart)
- **THEN** `AppEvent::Disconnected` SHALL be sent to the main event channel
- **THEN** status bar SHALL show grey `○ Reconnecting…`
- **THEN** the WS task SHALL wait `min(2^attempt, 30)` seconds and retry
- **THEN** on reconnect success, `AppEvent::Connected` SHALL be sent and status bar SHALL restore green `●`

#### Scenario: Permanent failure (server never comes up)

- **WHEN** all reconnect attempts within a session fail indefinitely
- **THEN** the TUI SHALL remain open with `○ Disconnected` status
- **THEN** the user SHALL be able to quit via Ctrl+Q at any time

---

### Requirement: Outbound messages sent as JSON over WebSocket

The system SHALL serialize and send outbound messages using the Aether WS JSON protocol.

#### Scenario: User sends a chat message

- **WHEN** user presses Enter with non-empty input
- **THEN** the WS task SHALL send:
  ```json
  {"type":"message","text":"<user_input>","group":"maria"}
  ```

#### Scenario: User sends a forwarded slash command

- **WHEN** a slash command is forwarded (not local)
- **THEN** the WS task SHALL send:
  ```json
  {"type":"command","text":"<slash_text>","group":"maria"}
  ```

---

### Requirement: Inbound streaming messages render token-by-token

The system SHALL process inbound WS messages and route them to `AppEvent` variants for the main loop.

Supported inbound message types:
- `{"type":"chunk","text":"..."}` — streaming token, append to `streaming_buf`
- `{"type":"message","text":"...","message_id":"..."}` — complete message (non-streaming or stream end)
- `{"type":"typing","is_typing":true/false}` — typing indicator
- `{"type":"error","message":"..."}` — error from backend

#### Scenario: Streaming response renders live

- **WHEN** Aether sends a series of `chunk` messages
- **THEN** each chunk SHALL be appended to `streaming_buf` and the UI SHALL redraw
- **THEN** the last line of the chat area SHALL show accumulated text + `▋` suffix

#### Scenario: Stream completes

- **WHEN** Aether sends a final `message` type after streaming chunks
- **OR WHEN** a `message` type arrives without prior chunks (non-streaming mode)
- **THEN** `streaming_buf` SHALL be flushed and the message appended to the `messages` vec
- **THEN** `▋` cursor SHALL be removed

#### Scenario: Typing indicator

- **WHEN** `{"type":"typing","is_typing":true}` is received
- **THEN** the status bar SHALL show `Maria is typing…`
- **WHEN** `{"type":"typing","is_typing":false}` is received
- **THEN** the typing indicator SHALL be removed from the status bar

#### Scenario: Error from backend

- **WHEN** `{"type":"error","message":"..."}` is received
- **THEN** the error text SHALL be displayed in the chat area with a distinct red/warning style

---

### Requirement: Unknown message types handled gracefully

The system SHALL not crash on unrecognised WebSocket message types.

#### Scenario: Unknown type field received

- **WHEN** a JSON message with an unrecognised `type` field is received
- **THEN** the message SHALL be silently ignored
- **THEN** no panic or disconnect SHALL occur
