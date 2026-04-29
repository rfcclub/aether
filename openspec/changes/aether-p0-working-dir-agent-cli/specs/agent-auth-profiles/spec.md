# agent-auth-profiles Specification

## Purpose

Define per-agent provider authentication isolation — each agent maintains its own set of provider credentials, model preferences, and fallback chains in `~/.aether/agents/<name>/agent/`, independent of other agents and the global config.

## ADDED Requirements

### Requirement: Per-agent auth directory created on agent add

When `aether agent add <name>` executes, Aether SHALL create `~/.aether/agents/<name>/agent/` with three files: `auth-state.json`, `auth-profiles.json`, and `models.json`.

#### Scenario: Auth files scaffolded

- **WHEN** `aether agent add maria` completes
- **THEN** `~/.aether/agents/maria/agent/` SHALL contain `auth-state.json`, `auth-profiles.json`, and `models.json`

#### Scenario: auth-state.json has default active provider

- **WHEN** auth files are scaffolded
- **THEN** `auth-state.json` SHALL contain `{ "activeProvider": null, "activeModel": null }` indicating fallback to global config

### Requirement: auth-profiles.json stores credentials per provider

`auth-profiles.json` SHALL contain a JSON object mapping provider names to their credential configuration. Each entry SHALL have `mode` ("api_key", "oauth", or "token") and provider-specific fields (`apiKey`, `email`, etc.).

#### Scenario: API key profile stored

- **WHEN** agent `maria` is configured with an OpenRouter API key
- **THEN** `auth-profiles.json` SHALL contain `{ "openrouter": { "provider": "openrouter", "mode": "api_key", "apiKey": "sk-or-..." } }`

#### Scenario: Multiple providers per agent

- **WHEN** agent `aria` is configured with both OpenRouter and Anthropic credentials
- **THEN** `auth-profiles.json` SHALL contain both `"openrouter"` and `"anthropic"` entries

### Requirement: models.json stores model preferences per agent

`models.json` SHALL contain the agent's model preferences: a `primary` model ID, an ordered `fallbacks` array, and an `overrides` object for per-model parameter tuning.

#### Scenario: Primary model set

- **WHEN** `aether agent add maria --model "openrouter/anthropic/claude-sonnet-4-6"` completes
- **THEN** `models.json` SHALL contain `{ "primary": "openrouter/anthropic/claude-sonnet-4-6", "fallbacks": [], "overrides": {} }`

#### Scenario: Fallback chain defined

- **WHEN** agent has `models.json` with fallbacks `["openrouter/google/gemini-2.5-flash", "fireworks-ai/accounts/fireworks/models/deepseek-v3"]`
- **THEN** the `ProviderRouter` for that agent SHALL attempt models in order: primary first, then each fallback

#### Scenario: Per-model override parameters

- **WHEN** `models.json` has `"overrides": { "openrouter/anthropic/claude-sonnet-4-6": { "maxTokens": 4096, "temperature": 0.7 } }`
- **THEN** those parameters SHALL be applied when that specific model is used for this agent only

### Requirement: Agent auth overrides global provider config

When an agent has its own `auth-profiles.json` with a provider entry, that entry SHALL take precedence over the global provider configuration in `~/.aether/config.json` for that agent's LLM calls.

#### Scenario: Agent-specific API key used

- **WHEN** global config has `openrouter.api_key = "global-key"` and agent `maria`'s `auth-profiles.json` has `openrouter.apiKey = "maria-key"`
- **THEN** agent `maria`'s LLM calls SHALL use `"maria-key"`, while other agents SHALL use `"global-key"`

#### Scenario: Agent has no override, falls back to global

- **WHEN** agent `maria`'s `auth-profiles.json` has no `openrouter` entry
- **THEN** agent `maria`'s LLM calls SHALL use the global `openrouter.api_key` from `config.json`

### Requirement: Auth profiles loaded at agent resolution time

`ConfigLoader` SHALL load an agent's auth profiles when resolving that agent for a message or heartbeat tick. The profiles SHALL be cached for the agent's lifetime but reloaded if the files are modified.

#### Scenario: Auth profiles loaded on first agent use

- **WHEN** the first message is routed to agent `maria` after startup
- **THEN** `ConfigLoader` SHALL read `~/.aether/agents/maria/agent/auth-profiles.json` and `models.json`

#### Scenario: Auth files not required for agent operation

- **WHEN** `~/.aether/agents/maria/agent/auth-profiles.json` does not exist
- **THEN** agent `maria` SHALL operate with global config only — no error SHALL be thrown

### Requirement: Auth directory permissions restricted

On Linux/macOS, the `~/.aether/agents/<name>/agent/` directory SHALL be created with `chmod 700` (owner read/write/execute only). On Windows, no permission change SHALL be attempted.

#### Scenario: Directory has restricted permissions on Linux

- **WHEN** `aether agent add maria` completes on Linux
- **THEN** `~/.aether/agents/maria/agent/` SHALL have permissions `700` (drwx------)

#### Scenario: Existing files not re-permissioned

- **WHEN** `~/.aether/agents/maria/agent/` already exists with files
- **THEN** Aether SHALL NOT modify permissions of existing files
