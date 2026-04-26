## ADDED Requirements

### Requirement: Abstract base for Anthropic API providers
`AnthropicCompatibleProviderBase` SHALL be an abstract class implementing `ILLMProvider` for Anthropic API format.

#### Scenario: New Anthropic-compatible provider
- **WHEN** developer creates provider using Anthropic API format
- **THEN** they inherit from `AnthropicCompatibleProviderBase` and implement abstract members

### Requirement: Anthropic-specific headers
Base class SHALL set `x-api-key`, `anthropic-version` headers on all requests.

#### Scenario: API version header
- **WHEN** request is sent to Anthropic endpoint
- **THEN** `anthropic-version: 2023-06-01` header is included

### Requirement: Tool use conversion
Base class SHALL convert tools to Anthropic `tool_use` format in message content blocks.

#### Scenario: Tool call conversion
- **WHEN** `LlmRequest.Tools` is provided
- **THEN** base class converts to `{"type": "tool_use", "id": "...", "name": "...", "input": {...}}`