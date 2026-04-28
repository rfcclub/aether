# gateway Specification

## Purpose
TBD - created by archiving change aether-full-project. Update Purpose after archive.
## Requirements
### Requirement: WebSocket channel accepts connections
The gateway SHALL accept WebSocket connections on a configurable port (default 8080) and normalize each received text frame into an `InboundMessage`.

#### Scenario: Client connects and sends a message
- **WHEN** a WebSocket client connects and sends a UTF-8 JSON frame `{"text": "hello", "session": "s1"}`
- **THEN** the gateway SHALL enqueue an `InboundMessage` with `Source="websocket"`, `GroupFolder="s1"`, `Text="hello"` to `IMessageQueue`

#### Scenario: Client disconnects cleanly
- **WHEN** a connected WebSocket client sends a close frame
- **THEN** the gateway SHALL close the connection gracefully without error and log the disconnect

#### Scenario: Malformed frame received
- **WHEN** a WebSocket frame cannot be deserialized as `InboundMessage`
- **THEN** the gateway SHALL send an error response frame and keep the connection open

### Requirement: Telegram channel polls for updates
The gateway SHALL poll the Telegram Bot API for updates on a configurable interval (default 2 seconds) when a bot token is configured.

#### Scenario: Telegram message received
- **WHEN** the Telegram poller receives a new text message
- **THEN** the gateway SHALL normalize it to `InboundMessage` with `Source="telegram"`, `GroupFolder` set to chat ID, and enqueue to `IMessageQueue`

#### Scenario: No bot token configured
- **WHEN** `telegram:bot_token` is absent from configuration
- **THEN** the gateway SHALL skip Telegram polling and log a warning at startup

### Requirement: Gateway routes responses back to originating channel
After `AetherSoul` produces a response, the gateway SHALL route it back to the channel and session that originated the message.

#### Scenario: WebSocket response delivery
- **WHEN** `AetherSoul` returns `AgentResponse` for a WebSocket-originated message
- **THEN** the gateway SHALL send the response text as a UTF-8 JSON frame to the originating connection

#### Scenario: Telegram response delivery
- **WHEN** `AetherSoul` returns `AgentResponse` for a Telegram-originated message
- **THEN** the gateway SHALL call the Telegram `sendMessage` API with `chat_id` and response text

