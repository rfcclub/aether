# ws-command-forward — Specification

> Capability: Backend handles `command` WS message type by forwarding to SlashCommandHandler.

---

## ADDED Requirements

### Requirement: `command` message type routed to SlashCommandHandler

The system SHALL process `{"type":"command","text":"<slash_text>","group":"<group>"}` and forward to the existing `SlashCommandHandler`, responding with the result text.

#### Scenario: Valid slash command forwarded

- **WHEN** a WS client sends `{"type":"command","text":"/model deepseek/deepseek-r1","group":"maria"}`
- **THEN** the backend SHALL build a `SlashCommandContext` with `ChannelName="websocket"`, `ChatId=<conn.ChatId>`, `Group="maria"`, `AgentName="maria"`, `Text="/model deepseek/deepseek-r1"`
- **THEN** the backend SHALL call `SlashCommandHandler.HandleAsync(context, ct)`
- **THEN** if the result is non-null, the backend SHALL respond:
  ```json
  {"type":"message","text":"<result_text>","message_id":"<guid>"}
  ```

#### Scenario: Unknown slash command

- **WHEN** `HandleAsync` returns null (command not recognised)
- **THEN** the backend SHALL respond:
  ```json
  {"type":"message","text":"Unknown command","message_id":"<guid>"}
  ```
- **THEN** no error type message SHALL be sent

#### Scenario: Missing text field

- **WHEN** `{"type":"command"}` is sent without a `text` field
- **THEN** the backend SHALL respond with `{"type":"error","message":"Missing 'text' field in command"}`

---

### Requirement: Command execution uses existing group session

The system SHALL route the command to the correct group context, consistent with how `ChannelMessageProcessor` handles slash commands.

#### Scenario: /model command changes model globally

- **WHEN** client sends `{"type":"command","text":"/model google/gemini-2.5-flash","group":"maria"}`
- **THEN** after successful `HandleAsync`, the model SHALL be changed in `ProviderRouter`
- **THEN** subsequent `/models` commands (either WS or Telegram) SHALL reflect the new model

#### Scenario: /new command resets session

- **WHEN** client sends `{"type":"command","text":"/new","group":"maria"}`
- **THEN** `HandleAsync` SHALL create a new session for the "maria" group
- **THEN** the response SHALL confirm the session reset

---

### Requirement: `command` handler does not block other message processing

The system SHALL handle `command` messages asynchronously without blocking the WS receive loop.

#### Scenario: Long-running command does not block pings

- **WHEN** a `command` message is processing (e.g. `/compact` which may be slow)
- **THEN** a concurrent `ping` from the same client SHALL still receive a `pong` response
- **THEN** no deadlock or timeout SHALL occur on the connection
