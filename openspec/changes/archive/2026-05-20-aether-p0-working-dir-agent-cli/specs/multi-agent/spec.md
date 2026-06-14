# multi-agent Specification (Delta)

## Purpose

Extend the multi-agent specification with channel binding persistence, per-agent authentication isolation, and the agent lifecycle CLI. The existing configuration-based routing is preserved and enhanced with binding-based resolution.

## MODIFIED Requirements

### Requirement: Agent selected by channel source via configuration

The gateway SHALL route each inbound message to the agent whose `bindings` array in `~/.aether/config.json` contains a match for `<channel_type>:<chat_id>`. If no binding matches, the message SHALL route to the agent named `"default"` (or the first agent with `enabled: true`). The legacy `gateway.agents.<source>` key in `appsettings.json` SHALL be deprecated in favor of bindings.

#### Scenario: Channel-specific agent via binding

- **WHEN** agent `maria` has binding `telegram:123456789` in `config.json` and a Telegram message arrives from chat `123456789`
- **THEN** the gateway SHALL dispatch the message to the `AetherSoul` instance registered as `"maria"`

#### Scenario: No binding, fallback to default

- **WHEN** no agent binding matches the incoming channel and chat ID
- **THEN** the gateway SHALL dispatch to the agent named `"default"` or the first agent with `enabled: true`

#### Scenario: Bound agent not registered in DI

- **WHEN** a binding references agent `maria` but `maria` is not in the DI container
- **THEN** the gateway SHALL log an error, fall back to the default agent, and respond with a static error message to the channel

#### Scenario: Legacy gateway.agents key still works

- **WHEN** `gateway.agents.telegram = "aria"` is configured in `appsettings.json` and no binding exists for the inbound Telegram chat
- **THEN** the gateway SHALL dispatch to agent `"aria"` and log a deprecation warning

### Requirement: Agent definitions declarable in configuration

Agents SHALL be definable in `~/.aether/config.json` under the `agents` section with optional per-agent settings: workspace path, model override, model fallbacks, channel bindings, heartbeat interval, and enabled flag. The CLI (`aether agent add/list/delete`) SHALL be the primary management interface.

#### Scenario: Agent with custom model

- **WHEN** `~/.aether/config.json` defines `agents.maria.model.primary = "openrouter/anthropic/claude-sonnet-4-6"`
- **THEN** the `maria` agent's `AetherSoul` SHALL use that model for LLM calls, overriding the global default

#### Scenario: Agent with fallback chain

- **WHEN** `agents.maria.model.fallbacks = ["openrouter/google/gemini-2.5-flash", "fireworks-ai/deepseek-v3"]`
- **THEN** `ProviderRouter` for agent `maria` SHALL attempt models in order: primary → first fallback → second fallback

#### Scenario: Disabled agent skipped in routing

- **WHEN** `agents.maria.enabled = false` in `config.json`
- **THEN** agent `maria` SHALL NOT receive routed messages and SHALL NOT appear in `aether agent list` by default

### Requirement: Named agents have isolated session contexts

Each named agent SHALL maintain its own session namespace so sessions from different agents do not overlap. The session namespace SHALL use the agent name as a prefix.

#### Scenario: Same chat ID, different agents

- **WHEN** channel `telegram` receives messages from group `"chat-123"` bound to agent `maria` and channel `websocket` receives messages from group `"chat-123"` bound to agent `aria`
- **THEN** each agent SHALL maintain a separate session (different `session_id`) in the database

#### Scenario: Agent shares same SQLite database

- **WHEN** multiple named agents are active
- **THEN** all agents SHALL share the same `aether.db` file with sessions disambiguated by `group_folder` prefix (`<agent_name>/<group_folder>`)

## ADDED Requirements

### Requirement: Channel bindings persisted in config.json

Agent channel bindings SHALL be stored as a `bindings` array of strings in the agent's entry in `~/.aether/config.json`. Each string SHALL use the format `<channel_type>:<chat_id>`. Bindings SHALL survive Aether restarts.

#### Scenario: Bindings loaded on startup

- **WHEN** Aether starts and `config.json` contains `agents.maria.bindings = ["telegram:123456789"]`
- **THEN** the `MessageRouter` SHALL route inbound Telegram messages from chat `123456789` to agent `maria`

#### Scenario: Bindings modified via CLI survive restart

- **WHEN** `aether agent bind maria --channel discord:guild456` is executed, then Aether restarts
- **THEN** agent `maria` SHALL still have the `discord:guild456` binding after restart

### Requirement: Per-agent provider credentials isolated

Each agent SHALL maintain its own provider credentials in `~/.aether/agents/<name>/agent/auth-profiles.json`. The credential files SHALL NOT be shared between agents.

#### Scenario: Agent uses own API key

- **WHEN** agent `maria` has `auth-profiles.json` with `openrouter.apiKey = "key-A"` and agent `aria` has `auth-profiles.json` with `openrouter.apiKey = "key-B"`
- **THEN** LLM calls for `maria` SHALL use `key-A` and LLM calls for `aria` SHALL use `key-B`

#### Scenario: Agent with no auth profile uses global config

- **WHEN** agent `maria` has no `auth-profiles.json` in its agent directory
- **THEN** the agent SHALL use the global provider credentials from `config.json`

### Requirement: Agent lifecycle managed via CLI

The `aether agent` CLI command group SHALL be the primary interface for agent lifecycle management. Direct editing of `config.json` SHALL also be supported, with the CLI providing validation and scaffolding that manual editing does not.

#### Scenario: Agent created via CLI is fully operational

- **WHEN** `aether agent add maria` completes and Aether is started
- **THEN** agent `maria` SHALL be resolvable by `IAgentProfile`, loadable by `AetherSoul`, and ready to process messages

#### Scenario: Agent deleted via CLI is fully removed

- **WHEN** `aether agent delete maria --prune-workspace --force` completes
- **THEN** no trace of agent `maria` SHALL remain in `config.json` or on disk in `~/.aether/workspaces/maria/`
