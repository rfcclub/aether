## Why

Tool call parameters from LLMs arrive as arbitrary JSON. A malformed or malicious parameter can crash the tool executor or bypass path validation. Validating parameters against JSON schemas before execution prevents crashes, tightens the sandbox, and gives the LLM actionable error feedback when it generates invalid tool calls.

## What Changes

- Add JSON Schema validation to tool calls using NJsonSchema
- Associate each tool definition with an optional JSON Schema for parameter validation
- Validate tool call arguments against the schema before execution
- Return structured validation errors to the LLM when parameters are invalid (instead of raw tool failure)
- Extend `LlmTool` and tool registry to carry optional schema definitions
- Wire validation into `AetherSoul` tool loop so validation failures become tool-result messages the LLM can self-correct from

## Capabilities

### New Capabilities
- `tool-parameter-validation`: Validate tool call arguments against JSON Schema before sandboxed execution; return structured errors the LLM can self-correct

### Modified Capabilities
<!-- None — this is additive. Existing tool executor behavior is unchanged; validation is a new layer. -->

## Impact

- `Agent/AetherSoul.cs` — wire validation into the tool loop
- `Agent/ToolExecutor.cs` (or new `Tooling/ParameterValidator.cs`) — validation logic
- `LlmTool` record — add optional `SchemaJson` or `JsonSchema` property
- `LlmToolCall` or validation model — add structured validation error type
- `Aether.csproj` — add `NJsonSchema` NuGet dependency
- All built-in tool definitions — add JSON Schemas for parameter validation
