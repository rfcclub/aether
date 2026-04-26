## Why

FireworksProvider and AnthropicProvider share common HTTP client patterns - auth headers, JSON serialization, error handling. Extracting to base classes reduces duplication and makes adding new providers trivial.

## What Changes

- Create `OpenAiCompatibleProviderBase` abstract class for OpenAI-compatible providers
- Create `AnthropicCompatibleProviderBase` abstract class for Anthropic API providers
- Refactor existing providers to inherit from base classes
- Add generic provider option for unknown/custom endpoints

## Capabilities

### New Capabilities

- `openai-compatible-provider-base`: Abstract base for OpenAI-compatible providers (Fireworks, local Ollama, etc.)
- `anthropic-compatible-provider-base`: Abstract base for Anthropic API providers (Claude direct, etc.)
- `generic-http-provider`: Generic provider for custom/unknown endpoints with configurable base URL

### Modified Capabilities

- `fireworks-provider`: Refactor to inherit from OpenAiCompatibleProviderBase
- `anthropic-provider`: Refactor to inherit from AnthropicCompatibleProviderBase

## Impact

- New: Base classes in Providers/ directory
- Modified: FireworksProvider, AnthropicProvider simplified
- New: GenericProvider for custom endpoints