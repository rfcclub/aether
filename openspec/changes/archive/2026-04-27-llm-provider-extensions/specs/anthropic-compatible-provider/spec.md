## ADDED Requirements

### Requirement: Anthropic API-compatible client
`AnthropicCompatibleProvider` SHALL implement ILLMProvider using Anthropic API format (messages endpoint, not chat completions).

#### Scenario: Chat completion via Anthropic API
- **WHEN** `CompleteAsync` is called with `LlmRequest`
- **THEN** the provider SHALL call Anthropic `/v1/messages` endpoint with proper headers (x-api-key, anthropic-version)

### Requirement: Claude tool use (beta)
`AnthropicCompatibleProvider` SHALL support Claude's tool use extension for function calling.

#### Scenario: Tool call with Claude
- **WHEN** `LlmRequest.Tools` is provided
- **THEN** the provider SHALL convert tools to Anthropic tool_use format and set `tool_choice: {type: "auto"}`

### Requirement: Token counting
`AnthropicCompatibleProvider` SHALL return accurate token usage from API response headers.

#### Scenario: Token usage recorded
- **WHEN** Anthropic API returns response with `x-consumer-token-usage` header
- **THEN** the provider SHALL extract input/output tokens and include in `LlmResponse` metadata