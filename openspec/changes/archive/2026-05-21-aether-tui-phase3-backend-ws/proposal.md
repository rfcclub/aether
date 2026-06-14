## Why

The `aether-tui` Rust client (Phase 1 + 2) needs three new WebSocket message types that the current `WebSocketChannel.cs` backend does not handle: `list_models` (populate model picker), `get_history` (resume conversation), and `command` (forward slash commands). Without these backend handlers, the Phase 2 TUI features silently no-op. This change adds the three handlers to `WebSocketChannel.cs` and wires them through the existing service layer.

## What Changes

- **MODIFY** `src/Aether/Channels/WebSocketChannel.cs` — add three new branches in `ProcessIncomingJsonAsync()`:
  1. `list_models` → query `IProviderRouter.GetAvailableModels()` + effective model, return `{"type":"models","current":"...","think_effort":"...","providers":[...]}`
  2. `get_history` → call `ISessionManager.GetOrCreateSessionAsync(group)` → `GetHistoryAsync(session.Id, maxTokens: 20000)`, return `{"type":"history","messages":[...]}`
  3. `command` → build `SlashCommandContext` → `ISlashCommandHandler.HandleAsync()` → return result as `{"type":"message","text":"...","message_id":"..."}`

## Capabilities

### New Capabilities

- `ws-list-models`: Backend handles `{"type":"list_models"}` from TUI and returns structured provider + model list
- `ws-get-history`: Backend handles `{"type":"get_history","group":"...","limit":N}` and returns recent message history
- `ws-command-forward`: Backend handles `{"type":"command","text":"/model ...","group":"..."}` and routes through existing slash command handler

### Modified Capabilities

*(no existing spec requirements change — this extends the WS channel with new message type branches)*

## Impact

- `src/Aether/Channels/WebSocketChannel.cs` — only file modified
- Requires `IProviderRouter` to be injected into `WebSocketChannel` (may already be injected — verify)
- Requires `ISlashCommandHandler` to be injected (verify existing DI wiring)
- No schema or DB changes
- No new NuGet packages
- All existing 454 tests must still pass — new code is additive only
