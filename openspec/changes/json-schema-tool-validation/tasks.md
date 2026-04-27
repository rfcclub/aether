## 1. Dependencies and Model

- [ ] 1.1 Add `NJsonSchema` NuGet package to Aether.csproj
- [ ] 1.2 Add `SchemaJson` property to `LlmTool` record (optional, nullable string)

## 2. Built-in Tool Schemas

- [ ] 2.1 Add JSON Schema for `read` tool (path: string, required)
- [ ] 2.2 Add JSON Schema for `write` tool (path: string, content: string, both required)
- [ ] 2.3 Add JSON Schema for `edit` tool (path: string, old: string, new: string, all required)
- [ ] 2.4 Add JSON Schema for `glob` tool (pattern: string required, root: string optional)
- [ ] 2.5 Add JSON Schema for `grep` tool (path: string, pattern: string, context_lines: string optional, required: path, pattern)
- [ ] 2.6 Add JSON Schema for `bash` tool (command: string required, cwd: string optional)
- [ ] 2.7 Verify each schema is consistent with the tool's `ParametersJson`

## 3. Validation Logic

- [ ] 3.1 Create `Tooling/ParameterValidator.cs` with `ValidateAsync(ToolCall, LlmTool)` method
- [ ] 3.2 Compile JSON Schemas at tool registration time (cache compiled validators)
- [ ] 3.3 Implement validation error formatting: JSON path + message, one per line
- [ ] 3.4 Return `ToolResult(false, formattedErrors)` for validation failures
- [ ] 3.5 Skip validation when `SchemaJson` is null or empty

## 4. Wire into AetherSoul Loop

- [ ] 4.1 In `RunLlmToolLoopAsync`, resolve the `LlmTool` for each tool call
- [ ] 4.2 Call `ParameterValidator.ValidateAsync` before `_tools.ExecuteAsync`
- [ ] 4.3 On validation failure, create error `ToolResult` and append as tool message (skip execution)
- [ ] 4.4 On validation success, proceed with normal execution flow

## 5. Tests

- [ ] 5.1 Test: tool with valid schema compiles without error
- [ ] 5.2 Test: tool with invalid schema throws at registration
- [ ] 5.3 Test: valid arguments pass validation
- [ ] 5.4 Test: missing required property returns validation error (not executed)
- [ ] 5.5 Test: wrong type returns validation error with path
- [ ] 5.6 Test: multiple validation errors all reported
- [ ] 5.7 Test: tool without schema skips validation, proceeds to execution
- [ ] 5.8 Test: LLM receives validation error and can retry with corrected parameters
- [ ] 5.9 Test: all 6 built-in tools have non-empty SchemaJson
