## ADDED Requirements

### Requirement: Shared agent defaults

Aether SHALL parse `agents.defaults` from `~/.aether/config.json` as a shared model baseline that applies to all agents that don't override their own model config.

#### Scenario: Agent inherits primary from defaults
- **WHEN** `agents.defaults.model.primary` is set to `"fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo"`
- **AND** an agent has no `model.primary`
- **THEN** that agent's effective primary model SHALL be the defaults primary

#### Scenario: Agent inherits fallbacks from defaults
- **WHEN** `agents.defaults.model.fallbacks` contains `["google/gemini-2.5-flash-lite"]`
- **AND** an agent has no `model.fallbacks`
- **THEN** that agent's effective fallback chain SHALL include the defaults fallbacks

#### Scenario: Agent overrides take precedence
- **WHEN** an agent has its own `model.primary`
- **AND** `agents.defaults.model.primary` is also set
- **THEN** the agent's own primary SHALL take precedence

#### Scenario: defaults is not treated as a real agent
- **WHEN** `agents.defaults` is present in config
- **THEN** it SHALL NOT appear in the agents dictionary after loading

### Requirement: Provider-slug model resolution

`ProviderRouter.ResolveModelToProvider` SHALL resolve model identifiers with a provider-slug prefix (e.g., `fireworks-ai/model-id`) to the matching registered provider.

#### Scenario: hyphen-slug resolves to provider
- **WHEN** the model identifier is `"fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo"`
- **AND** a provider named `"fireworks"` is registered
- **THEN** `ResolveModelToProvider` SHALL return the `"fireworks"` provider

#### Scenario: exact prefix match takes precedence
- **WHEN** the model identifier is `"openrouter/deepseek/deepseek-r1:free"`
- **AND** a provider named `"openrouter"` is registered
- **THEN** `ResolveModelToProvider` SHALL return the `"openrouter"` provider

#### Scenario: no match returns null
- **WHEN** the model identifier is `"unknown-slug/model-id"`
- **AND** no provider matches
- **THEN** `ResolveModelToProvider` SHALL return null

### Requirement: Models list preserved through merge

`ConfigLoader.MergeProviderFields` SHALL preserve the `Models` list from overrides when both base and overrides have non-empty models lists.

#### Scenario: Overrides models take precedence
- **WHEN** base has `Models: ["model-a"]` and overrides has `Models: ["model-b"]`
- **THEN** merged result SHALL have `Models: ["model-b"]`

#### Scenario: Base models preserved when overrides empty
- **WHEN** base has `Models: ["model-a"]` and overrides has `Models: null`
- **THEN** merged result SHALL have `Models: ["model-a"]`
