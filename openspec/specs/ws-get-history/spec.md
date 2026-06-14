# ws-get-history — Specification

> Capability: Backend handles `get_history` WS message and returns recent message history for a group.

---

## ADDED Requirements

### Requirement: `get_history` message type handled in WebSocketChannel

The system SHALL process `{"type":"get_history","group":"<group>","limit":<N>}` and respond with the conversation history for that group.

#### Scenario: History exists for group

- **WHEN** a WS client sends `{"type":"get_history","group":"maria","limit":50}`
- **THEN** the backend SHALL call `ISessionManager.GetOrCreateSessionAsync("maria")` to get or create the session
- **THEN** the backend SHALL call `GetHistoryAsync(session.Id, maxTokens: 20000)`
- **THEN** the backend SHALL apply the `limit` as an additional cap: `messages.Take(limit)`
- **THEN** the backend SHALL respond to that client only with:
  ```json
  {
    "type": "history",
    "messages": [
      {"role": "user", "content": "xin chào", "timestamp": "2026-05-20T21:00:00Z"},
      {"role": "assistant", "content": "Xin chào anh!", "timestamp": "2026-05-20T21:00:01Z"}
    ]
  }
  ```
- **THEN** messages SHALL be in chronological order (oldest first)

#### Scenario: No history for group (new session)

- **WHEN** the session exists but has no messages
- **OR WHEN** a new session is created
- **THEN** the backend SHALL respond with `{"type":"history","messages":[]}`
- **THEN** no error SHALL be sent

#### Scenario: Missing group field defaults to "main"

- **WHEN** the client sends `{"type":"get_history"}` without a `group` field
- **THEN** the backend SHALL use `"main"` as the default group name

---

### Requirement: `get_history` is non-destructive

The system SHALL not modify, compact, or delete any messages when responding to `get_history`.

#### Scenario: History unchanged after request

- **WHEN** `get_history` is called
- **THEN** the session message store SHALL be identical before and after the call
- **THEN** subsequent `get_history` calls SHALL return the same messages plus any new ones sent after
