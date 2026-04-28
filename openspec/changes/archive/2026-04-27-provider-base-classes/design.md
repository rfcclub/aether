## Context

Two provider patterns emerge: OpenAI-compatible (chat/completions endpoint) and Anthropic (messages endpoint). Both share HTTP client setup, auth, JSON handling.

## Goals / Non-Goals

**Goals:**
- Extract common HTTP patterns to abstract base classes
- Make adding new providers a matter of implementing 2-3 abstract methods
- Support generic/unknown endpoints via configurable provider

**Non-Goals:**
- Full abstraction of all provider differences
- Dynamic provider loading at runtime
- Provider SDK versioning

## Decisions

### OpenAiCompatibleProviderBase

Abstract methods:
- `GetModel()` → string
- `GetEndpoint()` → string (default: "chat/completions")
- `MapMessage(LlmMessage)` → object (override for custom formats)
- `MapTool(LlmTool)` → object

Properties:
- `ApiKey`, `BaseUrl`, `HttpClient`

### AnthropicCompatibleProviderBase

Abstract methods:
- `GetModel()` → string
- `GetEndpoint()` → string (default: "messages")

Properties:
- `ApiKey`, `BaseUrl`, `AnthropicVersion` (default: "2023-06-01")

### GenericHttpProvider

For unknown/custom endpoints:
- Configurable `BaseUrl`, `Endpoint`, `AuthHeader`
- JSON payload/response mapping via delegates
- Fallback for providers not explicitly supported

## Risks / Trade-offs

- [Risk] Base class changes break all providers → Mitigation: Keep base stable, add extension points
- [Risk] Over-abstraction hides important differences → Mitigation: Only abstract common patterns, keep provider-specific logic in subclass