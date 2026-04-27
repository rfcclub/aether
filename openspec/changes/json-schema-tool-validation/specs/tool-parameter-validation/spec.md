## ADDED Requirements

### Requirement: Tools carry optional JSON Schema for validation
Each `LlmTool` SHALL support an optional `SchemaJson` property. When present, the schema MUST be a valid JSON Schema document. The system SHALL compile the schema to a validator at tool registration time.

#### Scenario: Tool with schema compiles successfully
- **WHEN** a `LlmTool` is created with a valid `SchemaJson`
- **THEN** the system compiles it to a validator without error and the tool is ready for use

#### Scenario: Tool with invalid schema throws at registration
- **WHEN** a `LlmTool` is created with a malformed `SchemaJson`
- **THEN** the system SHALL throw an `ArgumentException` at registration time with details about the schema error

#### Scenario: Tool without schema skips validation
- **WHEN** a `LlmTool` has no `SchemaJson` (null or empty)
- **THEN** validation is skipped and the tool call proceeds directly to execution

### Requirement: Tool call arguments are validated before execution
Before executing a tool call, the system SHALL validate the call's arguments against the tool's JSON Schema (if present). Validation MUST occur before the sandboxed process is started.

#### Scenario: Valid arguments pass validation
- **WHEN** a tool call's arguments match the tool's JSON Schema
- **THEN** validation passes and the call proceeds to execution

#### Scenario: Invalid arguments are rejected before sandbox
- **WHEN** a tool call's arguments violate the tool's JSON Schema (e.g., missing required property, wrong type)
- **THEN** the tool call is NOT executed and no sandbox process is started

### Requirement: Validation errors are returned as structured tool results
When validation fails, the system SHALL return a `ToolResult` with `Succeeded = false` and an error message listing each validation failure with its JSON path and description. The format MUST be parseable by an LLM for self-correction.

#### Scenario: Single validation error
- **WHEN** a tool call has one validation error (e.g., `$.path` is missing)
- **THEN** the tool result contains `Succeeded = false` and an error message describing the error path and reason

#### Scenario: Multiple validation errors
- **WHEN** a tool call has multiple validation errors
- **THEN** the tool result lists all errors with their paths, separated by newlines

### Requirement: LLM can self-correct from validation errors
Validation error tool results SHALL follow the same message format as execution error tool results, so the LLM's existing self-correction behavior applies without modification.

#### Scenario: LLM receives validation error and retries
- **WHEN** the LLM receives a validation error tool result
- **THEN** the error message is appended to the conversation as a tool message with role "tool"
- **THEN** the LLM can generate a corrected tool call in the next iteration of the tool loop

### Requirement: All built-in tools provide JSON Schemas
Every built-in tool (`read`, `write`, `edit`, `glob`, `grep`, `bash`) SHALL include a `SchemaJson` that precisely defines its expected parameters. The schema SHALL be consistent with the tool's `ParametersJson` sent to the LLM.

#### Scenario: Built-in tools are registered with schemas
- **WHEN** AetherSoul initializes its built-in tool definitions
- **THEN** all 6 built-in tools have non-empty `SchemaJson` properties

#### Scenario: Schema and ParametersJson describe the same shape
- **WHEN** a built-in tool's `SchemaJson` and `ParametersJson` are compared
- **THEN** they describe the same required fields, types, and constraints
