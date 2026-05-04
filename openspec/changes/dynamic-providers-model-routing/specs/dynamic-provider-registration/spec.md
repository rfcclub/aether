## ADDED Requirements

### Requirement: Providers loaded from config.json
The system SHALL read all provider definitions from `~/.aether/config.json` (providers section) and `appsettings.json` (providers section) at startup and instantiate corresponding `ILLMProvider` implementations without hardcoded provider registration.

#### Scenario: Config.json with multiple providers
- **WHEN** `~/.aether/config.json` contains `"providers": { "openrouter": { "type": "openai", ... }, "crof-ai": { "type": "openai", ... } }`
- **THEN** the system registers `OpenRouterProvider` and `GenericHttpProvider("crof-ai")` as `ILLMProvider` singletons

#### Scenario: No config.json file
- **WHEN** neither `~/.aether/config.json` nor `appsettings.json` contains any provider definitions
- **THEN** the system logs a warning and registers zero `ILLMProvider` instances

#### Scenario: Provider with anthropic type
- **WHEN** a provider entry has `"type": "anthropic"`
- **THEN** the system instantiates `AnthropicProvider` with the configured API key and base URL

### Requirement: Provider factory maps type to implementation
The system SHALL use a `ProviderFactory` that maps `SpecProviderEntry.Type` to the correct `ILLMProvider` implementation class.

#### Scenario: OpenAI-compatible type
- **WHEN** `SpecProviderEntry.Type` is `"openai"`, `"openrouter"`, or `"fireworks"`
- **THEN** the factory creates a `GenericHttpProvider` instance configured with the entry's BaseUrl, ApiKey, and Model

#### Scenario: Anthropic type
- **WHEN** `SpecProviderEntry.Type` is `"anthropic"`
- **THEN** the factory creates an `AnthropicProvider` instance configured with the entry's BaseUrl and ApiKey

#### Scenario: Unknown type
- **WHEN** `SpecProviderEntry.Type` is not `"openai"` or `"anthropic"`
- **THEN** the factory logs a warning and falls back to `GenericHttpProvider` with openai defaults

### Requirement: Multiple models per provider
Each `SpecProviderEntry` SHALL support an optional `"models"` array listing all model IDs this provider can serve. When omitted, the provider claims models matching its provider name prefix.

#### Scenario: Provider with explicit model list
- **WHEN** `config.json` has provider `"my-proxy": { "type": "openai", "models": ["gpt-4", "gpt-3.5", "claude-3-opus"] }`
- **THEN** the provider is eligible to serve any of those three model IDs

#### Scenario: Provider without model list
- **WHEN** `config.json` has provider `"openrouter": { "type": "openai" }` without a `"models"` field
- **THEN** the provider matches any model ID starting with `"openrouter/"` plus the string `"openrouter"` itself
