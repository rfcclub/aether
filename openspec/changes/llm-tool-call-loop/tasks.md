## 1. Extend LLM Provider Contracts

- [ ] 1.1 Add `LlmToolParameter` record with `Name`, `Description`, `Type` (default `"string"`)
- [ ] 1.2 Add `LlmToolDefinition` record with `Name`, `Description`, `Parameters`
- [ ] 1.3 Add `LlmToolCall` record with `Id`, `Name`, `Arguments` (`IReadOnlyDictionary<string, string>`)
- [ ] 1.4 Add `LlmToolResult` record with `ToolCallId`, `Content`, `IsError`
- [ ] 1.5 Extend `LlmRequest` with optional `Tools` (`IReadOnlyList<LlmToolDefinition>?`)
- [ ] 1.6 Extend `LlmMessage` with optional `ToolCalls` and `ToolResults` lists
- [ ] 1.7 Extend `LlmResponse` with `ToolCalls` (`IReadOnlyList<LlmToolCall>`)

## 2. OpenRouterProvider Tool Serialization

- [ ] 2.1 Serialize `request.Tools` into Anthropic `tools` array format in request body
- [ ] 2.2 Serialize `message.ToolCalls` as `content` array with `tool_use` blocks
- [ ] 2.3 Serialize `message.ToolResults` as `content` array with `tool_result` blocks (role: `user`)
- [ ] 2.4 Deserialize response `content` array: extract `text` block into `Content`, extract `tool_use` blocks into `ToolCalls`
- [ ] 2.5 Handle response with no `content` (null check, throw meaningful error)

## 3. AetherSoul Tool Loop

- [ ] 3.1 Define static `BuiltinToolDefinitions` list for all 6 tools with name, description, and parameter schemas
- [ ] 3.2 Include `BuiltinToolDefinitions` in every `LlmRequest`
- [ ] 3.3 Add `max_tool_iterations` to `AgentOptions` / config, default 10
- [ ] 3.4 Replace single `CompleteAsync` call with loop: call → check tool calls → execute → append results → repeat
- [ ] 3.5 Append intermediate assistant (tool_use) message to session after each LLM call with tool calls
- [ ] 3.6 Execute each tool call via `IToolExecutor.ExecuteAsync`, collect results
- [ ] 3.7 Append `user` message with `ToolResults` populated to messages and session
- [ ] 3.8 Exit loop and return text response when no tool calls returned
- [ ] 3.9 Exit loop on iteration limit, return synthetic response string

## 4. Tests

- [ ] 4.1 Smoke test: `LlmRequest` with tool definitions serializes to valid Anthropic format (JSON check)
- [ ] 4.2 Smoke test: response with `tool_use` content block deserializes into `LlmResponse.ToolCalls`
- [ ] 4.3 Smoke test: `AetherSoul` with a mock provider that returns one tool call then plain text — verify loop executes tool and returns text
- [ ] 4.4 Smoke test: iteration guard fires after `max_tool_iterations` rounds
