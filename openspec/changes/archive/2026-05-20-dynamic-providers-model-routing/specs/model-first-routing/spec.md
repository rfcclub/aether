## ADDED Requirements

### Requirement: Agent model list takes priority over provider ordering
The system SHALL route each request by trying the agent's configured model list in order (primary model first, then fallbacks) regardless of provider priority ordering. Provider priority SHALL only be used as a tiebreaker when multiple providers can serve the same model.

#### Scenario: Agent with primary and fallback models
- **WHEN** agent config has `"model": { "primary": "crof-ai/glm-5.1", "fallbacks": ["google/gemini-3.1-pro-preview"] }`
- **THEN** the router tries `crof-ai/glm-5.1` first and falls back to `google/gemini-3.1-pro-preview` only if the primary fails

#### Scenario: Primary model succeeds
- **WHEN** the primary model `crof-ai/glm-5.1` returns a successful response
- **THEN** fallback models are NOT tried

#### Scenario: All models fail
- **WHEN** all models in the agent's list (primary + all fallbacks) fail
- **THEN** the router throws `InvalidOperationException` with message indicating all models exhausted

### Requirement: Model name resolved to provider at call time
The system SHALL resolve each model ID to a registered `ILLMProvider` instance by matching the model ID prefix against provider names or explicit model lists.

#### Scenario: Prefix match resolution
- **WHEN** model ID is `"openrouter/deepseek/deepseek-v3"` and provider `"openrouter"` is registered
- **THEN** the router resolves to `OpenRouterProvider` because model ID starts with `"openrouter/"`

#### Scenario: Explicit model list match
- **WHEN** model ID is `"gpt-4"` and provider `"my-proxy"` has `"models": ["gpt-4", "gpt-3.5"]`
- **THEN** the router resolves to `"my-proxy"` provider

#### Scenario: Provider name as model ID
- **WHEN** model ID is `"fireworks"` (bare provider name, no slash)
- **THEN** the router resolves to the `"fireworks"` provider using that provider's default model

#### Scenario: Unresolvable model
- **WHEN** model ID is `"unknown-model-xyz"` and no provider name prefix matches
- **THEN** the router logs a warning and skips to the next fallback model

### Requirement: Per-agent model list from .aether.json
The system SHALL read each agent's model primary and fallback list from `{workspace}/.aether.json` under the `"model"` key.

#### Scenario: Full model config
- **WHEN** `.aether.json` contains `"model": { "primary": "model-a", "fallbacks": ["model-b", "model-c"] }`
- **THEN** the agent's model chain is `["model-a", "model-b", "model-c"]`

#### Scenario: No model config
- **WHEN** `.aether.json` has no `"model"` key
- **THEN** the agent uses the provider-level default model of the highest-priority provider

#### Scenario: Primary only, no fallbacks
- **WHEN** `.aether.json` contains `"model": { "primary": "model-a" }` with no fallbacks array
- **THEN** the agent's model chain is `["model-a"]` with no fallback beyond the single model
