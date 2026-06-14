## Context

`WebSocketChannel.cs` currently handles two message types in `ProcessIncomingJsonAsync()`: `message` (chat) and `ping`. Phase 3 adds three new cases to this single switch statement.

Existing services already registered in DI:
- `ProviderRouter` (concrete, registered as singleton) — has `GetAvailableModels()` returning `IReadOnlyList<(string Provider, string Model)>` and `EffectiveModel` string property
- `SlashCommandHandler` (concrete singleton) — has `HandleAsync(SlashCommandContext, CancellationToken)` returning `SlashCommandResult?`
- `ISessionManager` (singleton) — has `GetOrCreateSessionAsync(group)` + `GetHistoryAsync(sessionId, maxTokens)`

`WebSocketChannel` currently takes only `(int port, ILogger<WebSocketChannel> logger)` in its constructor — both new services must be injected.

## Goals / Non-Goals

**Goals:**
- Handle `list_models` → return structured provider+model JSON to the requesting WS client only
- Handle `get_history` → return last N messages for a group to the requesting WS client only
- Handle `command` → forward to `SlashCommandHandler`, return text result as a `message` type
- Keep existing 454 tests green (additive-only changes)

**Non-Goals:**
- Broadcasting model changes to all connected clients (single-client response only)
- Modifying `ProviderRouter` or `SlashCommandHandler`
- Adding new DI registrations beyond injecting into `WebSocketChannel`
- Pagination for history (single batch, `limit` param respected)

## Decisions

### D1: Inject `ProviderRouter` + `SlashCommandHandler` + `ISessionManager` into `WebSocketChannel`

**Decision:** Add three new constructor parameters to `WebSocketChannel`. Update `WebSocketChannelService.cs` (or wherever `WebSocketChannel` is instantiated) to pass them from DI.

**Rationale:** `WebSocketChannel` is already a singleton; these are all singletons — no scope issue.

**Alternative considered:** Using `IServiceProvider` + service locator inside the handler — rejected, harder to test and violates DI conventions.

---

### D2: `list_models` response format — grouped by provider

**Decision:** Response:
```json
{
  "type": "models",
  "current": "deepseek/deepseek-r1",
  "think_effort": "high",
  "providers": [
    {"name": "openrouter", "models": ["deepseek/deepseek-r1", "google/gemini-2.5-flash"]},
    {"name": "fireworks",  "models": ["accounts/fireworks/deepseek-v3"]}
  ]
}
```
Built by grouping `GetAvailableModels()` result (tuples of `(Provider, Model)`) with LINQ `GroupBy`.

`think_effort`: read from `ProviderRouter.ThinkEffort` if property exists, else `"default"`.

---

### D3: `get_history` — `maxTokens: 20000`, map to response format

**Decision:**
```json
{"type":"history","messages":[{"role":"user","content":"...","timestamp":"..."},…]}
```
Call `_sessionManager.GetOrCreateSessionAsync(group)` → `GetHistoryAsync(session.Id, maxTokens: 20000)`. Map `LlmMessage` list to anonymous objects with `role`, `content`, `timestamp` (if available, else omit).

`limit` param from TUI request respected as additional cap: `messages.Take(limit)`.

---

### D4: `command` handler — reuse `SlashCommandContext`, respond as `message` type

**Decision:** Build `SlashCommandContext` with same fields as `ChannelMessageProcessor` uses:
```csharp
new SlashCommandContext(
    ChannelName: "websocket",
    ChatId: conn.ChatId,
    Group: group,
    AgentName: group,
    Text: commandText,
    Channel: this,
    Services: _services)
```
Requires `IServiceProvider _services` injected. On `HandleAsync` returning non-null result, send result text as `{"type":"message","text":"...","message_id":"..."}`. On null result (command not recognised), send `{"type":"message","text":"Unknown command","message_id":"..."}`.

**Note on `IServiceProvider`:** `SlashCommandHandler` already resolves scoped services internally. We inject `IServiceProvider` (root) into `WebSocketChannel` — same pattern used in `ChannelMessageProcessor`.

## Risks / Trade-offs

| Risk | Mitigation |
|------|-----------|
| `ProviderRouter.ThinkEffort` property might not exist | Use reflection / null-check or check source before implementing; fall back to `"default"` string |
| `SlashCommandContext` constructor may change between Phase 2 and Phase 3 merge | Verify parameter list from `SlashCommandTypes.cs` immediately before writing code |
| `GetHistoryAsync` returning messages in oldest-first order vs newest-first | Check SessionManager ordering; TUI expects chronological (oldest first) |
| New constructor params break `WebSocketChannelService` instantiation | Update `WebSocketChannelService.cs` constructor call simultaneously |

## Migration Plan

1. Modify `WebSocketChannel.cs` constructor + `ProcessIncomingJsonAsync` switch
2. Update `WebSocketChannelService.cs` (or DI registration) to pass new deps
3. Run `dotnet test` — all 454 must pass
4. No DB migration, no config change, no restart procedure beyond normal redeploy

## Open Questions

- Does `ProviderRouter` expose a `ThinkEffort` property? If not, emit `"think_effort": null` in response
- What is the exact signature of `SlashCommandContext`? (Verify `SlashCommandTypes.cs` at implementation time)
