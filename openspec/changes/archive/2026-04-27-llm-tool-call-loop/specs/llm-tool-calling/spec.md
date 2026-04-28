## ADDED Requirements

### Requirement: Tool Definition Contract

`LlmRequest` MUST support an optional list of `LlmToolDefinition` describing available tools.

#### Scenario: Request with tools serialized
- **WHEN** `CompleteAsync` is called with `request.Tools` containing one or more definitions
- **THEN** the HTTP request body includes a `tools` array in Anthropic format with `name`, `description`, and `input_schema` for each tool

### Requirement: Tool Call Response

`LlmResponse` MUST carry any `tool_use` content blocks returned by the LLM.

#### Scenario: Response with tool calls deserialized
- **WHEN** the API response contains `content` blocks with `type: "tool_use"`
- **THEN** `LlmResponse.ToolCalls` contains one `LlmToolCall` per block with `Id`, `Name`, and `Arguments` populated

#### Scenario: Response with only text
- **WHEN** the API response contains only a `text` block
- **THEN** `LlmResponse.ToolCalls` is empty and `LlmResponse.Content` contains the text

### Requirement: Tool Result Message Serialization

`LlmMessage` MUST support carrying tool results so they can be sent back to the LLM.

#### Scenario: Tool result message serialized
- **WHEN** an `LlmMessage` has `ToolResults` populated
- **THEN** the message is serialized as `role: "user"` with `content` array containing `tool_result` blocks referencing the correct `tool_use_id`

### Requirement: Tool Definition Schema

`LlmToolDefinition` MUST include enough information to generate a valid Anthropic JSON schema.

#### Scenario: Tool with string parameters
- **WHEN** a tool definition has `Parameters` with string-typed properties
- **THEN** the serialized `input_schema` is a valid JSON Schema object with those properties listed
