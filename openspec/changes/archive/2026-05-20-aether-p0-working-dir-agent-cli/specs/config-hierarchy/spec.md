# config-hierarchy Specification

## Purpose

Define the five-layer configuration merge system that produces the effective Aether configuration. This enables framework defaults, user-global overrides, agent-specific settings, environment variable injection, and CLI flag overrides — all with deterministic precedence.

## ADDED Requirements

### Requirement: Five-layer configuration merge order

Aether SHALL merge configuration from five sources in order (later wins): (1) framework `appsettings.json`, (2) `~/.aether/config.json`, (3) `<workspace>/.aether.json`, (4) `AETHER_*` environment variables, (5) CLI flags.

#### Scenario: Global user config overrides framework default

- **WHEN** `appsettings.json` sets `llm.timeout_seconds = 90` and `~/.aether/config.json` sets `llm.timeout_seconds = 120`
- **THEN** the effective `llm.timeout_seconds` SHALL be `120`

#### Scenario: Agent config overrides global

- **WHEN** `~/.aether/config.json` sets `llm.model = "openrouter/auto"` and `<workspace>/.aether.json` sets `llm.model = "openrouter/claude-sonnet-4-6"`
- **THEN** the effective model for that agent SHALL be `"openrouter/claude-sonnet-4-6"`

#### Scenario: Environment variable overrides all file config

- **WHEN** all file config layers set `llm.api_key = "file-key"` and `AETHER_llm__api_key = "env-key"` is set
- **THEN** the effective `llm.api_key` SHALL be `"env-key"`

#### Scenario: CLI flag overrides everything

- **WHEN** `--model "openrouter/gpt-5.4"` is passed on the command line
- **THEN** the effective model SHALL be `"openrouter/gpt-5.4"` regardless of all file and env config values

#### Scenario: Missing intermediate layer is skipped

- **WHEN** `~/.aether/config.json` exists but `<workspace>/.aether.json` does not
- **THEN** the merge SHALL skip layer 3 and continue to env vars and CLI flags without error

### Requirement: ConfigLoader produces typed configuration objects

The `ConfigLoader` service SHALL produce strongly-typed configuration objects: `AetherAppConfig` (top-level), `AgentEntryConfig` (per-agent), and `AgentAuthConfig` (per-agent credentials). These objects SHALL be available via DI.

#### Scenario: AetherAppConfig populated from merge

- **WHEN** configuration is loaded with at least framework defaults
- **THEN** `AetherAppConfig` SHALL contain non-null values for `Providers`, `AgentDefaults`, and `Agents`

#### Scenario: AgentEntryConfig populated per agent

- **WHEN** `config.json` defines an agent `maria` with workspace path and model
- **THEN** `AetherAppConfig.Agents["maria"]` SHALL contain `Name = "maria"`, `Workspace` (absolute path), and `Model` (with Primary and Fallbacks)

#### Scenario: Agent not in config returns null

- **WHEN** `ConfigLoader.GetAgentConfig("nonexistent")` is called for an agent not in `config.json`
- **THEN** the method SHALL return `null`

### Requirement: Environment variable mapping preserves nested keys

Environment variables prefixed with `AETHER_` SHALL map to config keys using `__` (double underscore) as the nesting separator. Single underscore SHALL also work as a fallback for non-ambiguous keys.

#### Scenario: Double underscore maps to nested key

- **WHEN** `AETHER_llm__api_key = "sk-xxx"` is set
- **THEN** the effective config SHALL have `llm.api_key = "sk-xxx"`

#### Scenario: Single underscore fallback

- **WHEN** `AETHER_llm_api_key = "sk-yyy"` is set and no `AETHER_llm__api_key` exists
- **THEN** the effective config SHALL have `llm.api_key = "sk-yyy"`

#### Scenario: Non-AETHER env vars ignored

- **WHEN** `OPENROUTER_API_KEY = "sk-zzz"` is set without `AETHER_` prefix
- **THEN** it SHALL NOT be included in the Aether configuration

### Requirement: Config validation at load time

`ConfigLoader` SHALL validate required fields after merge. If `llm.api_key` is empty and no provider-specific key is set, `ConfigLoader` SHALL log a warning. If both `llm.provider` and `llm.api_key` are empty, it SHALL throw `InvalidOperationException`.

#### Scenario: Missing provider key warns

- **WHEN** merged config has no API key in any provider section
- **THEN** `ConfigLoader` SHALL log a warning: "No LLM provider API key configured. Set AETHER_llm__api_key or configure a provider in ~/.aether/config.json."

#### Scenario: Missing critical config throws

- **WHEN** merged config has neither `llm.provider` nor any provider-specific section (`fireworks`, `anthropic`) enabled
- **THEN** `ConfigLoader.LoadAsync()` SHALL throw `InvalidOperationException` with message: "No LLM provider configured."

### Requirement: Default config.json created on first wizard run

When the first-run wizard completes, `ConfigLoader` SHALL write `~/.aether/config.json` with the user's selections. The file SHALL include `meta.lastTouchedVersion` and `wizard.lastRunAt` metadata fields.

#### Scenario: config.json created by wizard

- **WHEN** the first-run wizard completes successfully
- **THEN** `~/.aether/config.json` SHALL exist with valid JSON containing `meta`, `wizard`, `providers`, and `agents` sections

#### Scenario: config.json has version metadata

- **WHEN** `~/.aether/config.json` is created
- **THEN** `meta.lastTouchedVersion` SHALL match the current Aether version and `wizard.lastRunAt` SHALL be an ISO 8601 timestamp
