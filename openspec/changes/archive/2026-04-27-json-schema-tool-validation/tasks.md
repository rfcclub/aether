## 1. Dependencies and Model

- [x] 1.1 Add `NJsonSchema` NuGet package to Aether.csproj
- [x] 1.2 Add `SchemaJson` property to `LlmTool` record (optional, nullable string)

## 2. Built-in Tool Schemas

- [x] 2.1 Add JSON Schema for `read` tool (path: string, required)
- [x] 2.2 Add JSON Schema for `write` tool (path: string, content: string, both required)
- [x] 2.3 Add JSON Schema for `edit` tool (path: string, old: string, new: string, all required)
- [x] 2.4 Add JSON Schema for `glob` tool (pattern: string required, root: string optional)
- [x] 2.5 Add JSON Schema for `grep` tool (path: string, pattern: string, context_lines: string optional, required: path, pattern)
- [x] 2.6 Add JSON Schema for `bash` tool (command: string required, cwd: string optional)
- [x] 2.7 Verify each schema is consistent with the tool's `ParametersJson`

## 3. Validation Logic

- [x] 3.1 Create `Tooling/ParameterValidator.cs` with `ValidateAsync(ToolCall, LlmTool)` method
- [x] 3.2 Compile JSON Schemas at tool registration time (cache compiled validators)
- [x] 3.3 Implement validation error formatting: JSON path + message, one per line
- [x] 3.4 Return `ToolResult(false, formattedErrors)` for validation failures
- [x] 3.5 Skip validation when `SchemaJson` is null or empty

## 4. Wire into AetherSoul Loop

- [x] 4.1 In `RunLlmToolLoopAsync`, resolve the `LlmTool` for each tool call
- [x] 4.2 Call `ParameterValidator.ValidateAsync` before `_tools.ExecuteAsync`
- [x] 4.3 On validation failure, create error `ToolResult` and append as tool message (skip execution)
- [x] 4.4 On validation success, proceed with normal execution flow

## 5. Tests

- [x] 5.1 Test: tool with valid schema compiles without error
- [x] 5.2 Test: tool with invalid schema throws at registration
- [x] 5.3 Test: valid arguments pass validation
- [x] 5.4 Test: missing required property returns validation error (not executed)
- [x] 5.5 Test: wrong type returns validation error with path
- [x] 5.6 Test: multiple validation errors all reported
- [x] 5.7 Test: tool without schema skips validation, proceeds to execution
- [x] 5.8 Test: LLM receives validation error and can retry with corrected parameters
- [x] 5.9 Test: all 6 built-in tools have non-empty SchemaJson
