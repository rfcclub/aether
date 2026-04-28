# openai-compatible-provider-base Specification

## Purpose
TBD - created by archiving change provider-base-classes. Update Purpose after archive.
## Requirements
### Requirement: Abstract base for OpenAI-compatible providers
`OpenAiCompatibleProviderBase` SHALL be an abstract class implementing `ILLMProvider` with common HTTP client patterns.

#### Scenario: New provider implementation
- **WHEN** developer creates new OpenAI-compatible provider (e.g., local Ollama)
- **THEN** they inherit from `OpenAiCompatibleProviderBase` and implement abstract members

### Requirement: Common HTTP patterns
Base class SHALL handle: auth header, JSON serialization, error handling, response parsing.

#### Scenario: HTTP error handling
- **WHEN** provider returns non-2xx status
- **THEN** base class SHALL throw `InvalidOperationException` with status and response body

### Requirement: Message mapping override
Subclasses SHALL be able to override `MapMessage` for custom message formats.

#### Scenario: Custom role mapping
- **WHEN** provider requires different role format (e.g., "user" vs "human")
- **THEN** subclass overrides `MapMessage` to customize

