## Why

`AetherSoul.ProcessAsync` makes a single LLM call and ignores `_tools`. The LLM cannot use tools because `LlmRequest` carries no tool definitions and `LlmResponse` has no tool-call fields. Without this loop, Aether is purely conversational — it cannot read files, run bash, or take any autonomous action.

## What Changes

- Extend `ILLMProvider` contracts: `LlmRequest` gains a `Tools` list; `LlmResponse` gains a `ToolCalls` list; add `LlmToolDefinition` and `LlmToolCall` records
- `OpenRouterProvider` maps these records to Anthropic tool-use format in JSON
- `AetherSoul.ProcessAsync` enters a tool loop: call LLM → if tool calls returned → execute each → append results as `tool` messages → call LLM again → repeat until no tool calls or max iterations
- Add max-iteration guard (default 10) to prevent infinite loops

## Capabilities

### New Capabilities

- `llm-tool-calling`: LLM provider contract and OpenRouter implementation for Anthropic-format tool use
- `aether-soul-tool-loop`: AetherSoul multi-turn tool execution loop

### Modified Capabilities

- (none — existing contracts are extended, not replaced)

## Impact

- **Modified**: `Providers/ILLMProvider.cs` — new record types and extended request/response
- **Modified**: `Providers/OpenRouterProvider.cs` — serialize tools in request, deserialize tool_use blocks in response
- **Modified**: `Agent/AetherSoul.cs` — replace single call with loop
- **Config**: `max_tool_iterations` in `appsettings.json` `sandbox` section (or new `agent` section)
