# openai-compatible-provider Specification

## Purpose
TBD - created by archiving change llm-provider-extensions. Update Purpose after archive.
## Requirements
### Requirement: OpenAI-compatible API client for Fireworks
`OpenApiCompatibleProvider` SHALL use OpenAI.NET SDK with Fireworks AI base URL to make chat completions.

#### Scenario: Chat completion request
- **WHEN** `CompleteAsync` is called with `LlmRequest`
- **THEN** the provider SHALL call Fireworks AI `/chat/completions` endpoint with OpenAI-compatible format

### Requirement: Streaming support
`OpenApiCompatibleProvider` SHALL support streaming when `LlmRequest.Stream` is true.

#### Scenario: Streaming response
- **WHEN** `LlmRequest.Stream = true`
- **THEN** the provider SHALL return `IAsyncEnumerable<string>` yielding tokens as they arrive

### Requirement: Tool calling support
`OpenApiCompatibleProvider` SHALL support tool/function calling via OpenAI format.

#### Scenario: Tool call request
- **WHEN** `LlmRequest.Tools` is provided with tool definitions
- **THEN** the provider SHALL pass tools in OpenAI function calling format

