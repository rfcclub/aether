# channel-binding Specification

## Purpose

Define how agents are bound to messaging channels (Telegram, Discord, WebSocket) via CLI and config persistence, and how inbound messages are resolved to the correct agent at routing time.

## ADDED Requirements

### Requirement: CLI binds channel to agent

`aether agent bind <name> --channel <type:chatId>` SHALL add a binding entry to the agent's `bindings` array in `~/.aether/config.json`. The binding SHALL use the format `<channel_type>:<chat_id>`.

#### Scenario: Telegram channel bound

- **WHEN** `aether agent bind maria --channel telegram:123456789` is executed
- **THEN** `config.json` `agents.maria.bindings` SHALL contain `"telegram:123456789"`

#### Scenario: Multiple channels bound to same agent

- **WHEN** `aether agent bind maria --channel telegram:chatA` and `aether agent bind maria --channel discord:guildB` are executed
- **THEN** `agents.maria.bindings` SHALL contain both `"telegram:chatA"` and `"discord:guildB"`

#### Scenario: Duplicate binding rejected

- **WHEN** `aether agent bind maria --channel telegram:123456789` is executed but that binding already exists
- **THEN** the command SHALL print "Binding 'telegram:123456789' already exists for agent 'maria'" and exit with non-zero code

#### Scenario: Channel already bound to another agent

- **WHEN** `aether agent bind aria --channel telegram:123456789` is executed but `telegram:123456789` is already bound to agent `maria`
- **THEN** the command SHALL print a warning: "Channel 'telegram:123456789' is currently bound to 'maria'. Reassigning." and move the binding

### Requirement: CLI unbinds channel from agent

`aether agent unbind <name> --channel <type:chatId>` SHALL remove the binding from the agent's `bindings` array.

#### Scenario: Binding removed

- **WHEN** `aether agent unbind maria --channel telegram:123456789` is executed for an existing binding
- **THEN** `config.json` `agents.maria.bindings` SHALL no longer contain `"telegram:123456789"`

#### Scenario: Nonexistent binding

- **WHEN** `aether agent unbind maria --channel telegram:nonexistent` is executed
- **THEN** the command SHALL print "Binding 'telegram:nonexistent' not found for agent 'maria'" and exit with non-zero code

### Requirement: MessageRouter resolves agent from bindings

The `MessageRouter` SHALL resolve which agent handles an inbound message by scanning all agents' `bindings` arrays for a match on `<channel_type>:<chat_id>`. If no binding matches, the message SHALL route to the default agent.

#### Scenario: Inbound message matched to bound agent

- **WHEN** a Telegram message arrives from `chat_id = 123456789` and agent `maria` has binding `telegram:123456789`
- **THEN** the message SHALL be routed to agent `maria`

#### Scenario: Inbound message with no binding

- **WHEN** a Telegram message arrives from `chat_id = 999999999` and no agent has that binding
- **THEN** the message SHALL be routed to the default agent (first agent with `enabled: true` or agent named "default")

#### Scenario: Binding scan is cached

- **WHEN** the first message is routed after startup
- **THEN** the binding resolution SHALL cache the scan result and reuse it for subsequent messages without re-reading `config.json` on every message

### Requirement: Bindings survive config reload

When `~/.aether/config.json` is modified externally (e.g., manual edit), the `MessageRouter` SHALL detect the change and invalidate its binding cache.

#### Scenario: Config change detected

- **WHEN** `config.json` is modified while Aether is running and the next message arrives
- **THEN** the `MessageRouter` SHALL re-read and re-cache bindings before resolving the agent

### Requirement: Agent bind command shows current bindings without arguments

When `aether agent bind <name>` is executed without `--channel`, the command SHALL list all current bindings for that agent.

#### Scenario: List bindings for agent

- **WHEN** `aether agent bind maria` is executed with no `--channel` flag
- **THEN** output SHALL list all bindings for agent `maria` in a table or bullet list format
