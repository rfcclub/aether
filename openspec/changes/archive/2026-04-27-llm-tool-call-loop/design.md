## Context

`ILLMProvider` uses flat `LlmMessage(Role, Content)` records and a single-turn request/response. OpenRouter supports Anthropic's tool-use format via `tools` array in the request and `tool_use` content blocks in the response. `AetherSoul` currently ignores `_tools` with a dead reference.

## Goals / Non-Goals

**Goals:**
- Add `LlmToolDefinition`, `LlmToolCall` records; extend `LlmRequest` and `LlmResponse`
- `OpenRouterProvider` serializes tool definitions in request; deserializes `tool_use` content blocks into `LlmToolCall` list in response
- `AetherSoul` loops: LLM call → execute tools → append `tool_result` messages → repeat
- Guard with configurable `max_tool_iterations` (default 10)

**Non-Goals:**
- Streaming responses (future)
- Parallel tool execution (future — sequential is correct for now)
- Tool definitions loaded from external registry (built-in tools only)

## Decisions

**D1 — Message content polymorphism**: Anthropic format uses array content blocks (`text`, `tool_use`, `tool_result`). To avoid a full content-block model, `LlmMessage` gains an optional `ToolCalls` list and an optional `ToolResults` list. `OpenRouterProvider` serializes these into the correct block format. Regular text messages use the existing `Content` string.

**D2 — Tool definitions inline in AetherSoul**: `AetherSoul` constructs the `tools` list from hardcoded metadata matching the 6 built-in tools (`bash`, `read`, `glob`, `grep`, `write`, `edit`). No dynamic registry needed at this stage.

**D3 — Tool results as special messages**: After executing tools, `AetherSoul` appends a `LlmMessage("user", ...)` with `ToolResults` populated (Anthropic format: role `user` with `tool_result` content blocks referencing the `tool_use_id`).

**D4 — Loop termination**: Stop when `response.ToolCalls` is empty/null, or when `maxIterations` is exceeded. On iteration limit, append a synthetic assistant message `"[Max tool iterations reached]"` and return.

**D5 — Session persistence for tool turns**: All intermediate `assistant` (tool_use) and `user` (tool_result) messages are appended to session history so context is preserved across turns.

## Risks / Trade-offs

- **OpenRouter model compatibility**: Not all models on OpenRouter support Anthropic tool format. This targets Claude models routed via OpenRouter. If provider is changed, the content-block serialization may need adjustment.
- **Tool result size**: Large tool outputs (e.g. full file reads) included in history will inflate token count. Session trimming (separate change) will address this.
- **Sequential tool execution**: Multiple tool calls in one response are executed one by one. This is safe and simple but slower than parallel.
