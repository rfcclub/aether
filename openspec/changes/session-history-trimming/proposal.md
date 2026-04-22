## Why

`AetherSoul` passes a hardcoded `maxMessages: 40` to `GetHistoryAsync`. As tool-use turns accumulate (each producing two messages — tool_use + tool_result — plus large outputs), history will bloat context well beyond model limits. A token-budget trim prevents 400 errors from context overflow without requiring full compaction infrastructure.

## What Changes

- Add `ITokenEstimator` interface and a fast character-based `CharTokenEstimator` implementation
- Add `TrimHistoryToTokenBudget(messages, budget)` helper (static utility)
- `AetherSoul.ProcessAsync` applies the token budget before building the `LlmRequest`, dropping oldest messages first while preserving the system prompt
- Add configurable `history_token_budget` to agent config (default: 80000 tokens)
- `GetHistoryAsync` continues to return up to `maxMessages`; trimming is done in the caller

## Capabilities

### New Capabilities

- `session-history-trimming`: Token-budgeted history trimming that drops oldest messages to stay within a configurable context budget

### Modified Capabilities

- (none)

## Impact

- **New files**: `Agent/ITokenEstimator.cs`, `Agent/CharTokenEstimator.cs`, `Agent/HistoryTrimmer.cs`
- **Modified**: `Agent/AetherSoul.cs` — apply trim before building LlmRequest
- **Config**: `agent.history_token_budget` in `appsettings.json`
