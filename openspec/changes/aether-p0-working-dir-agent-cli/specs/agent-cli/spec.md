# agent-cli Specification

## Purpose

Define the `aether agent` CLI command group for agent lifecycle management — adding, listing, deleting, and updating agent identity. This is the primary management surface for Aether operators.

## ADDED Requirements

### Requirement: Agent add command scaffolds workspace

`aether agent add <name>` SHALL create a new agent entry in `~/.aether/config.json` and scaffold a workspace directory at `~/.aether/workspaces/<name>/` with all required personality files populated from default templates.

#### Scenario: Agent created with all files

- **WHEN** `aether agent add maria` is executed on a system with `~/.aether/` initialized
- **THEN** `~/.aether/workspaces/maria/` SHALL exist containing: `SOUL.md`, `USER.md`, `IDENTITY.md`, `MEMORY.md`, `HEARTBEAT.md`, `AGENTS_GUARD.md`, `DREAMS.md`, `INTROSPECTION.md`, `TASK_INBOX.md`, `TASK_REPORT.md`, `.aether.json`, and `memory/` directory

#### Scenario: Agent registered in config.json

- **WHEN** `aether agent add maria` completes
- **THEN** `~/.aether/config.json` SHALL contain `agents.maria` with keys: `name`, `workspace` (absolute path), `model` (default model config), `bindings` (empty array), `enabled` (true)

#### Scenario: Custom workspace path

- **WHEN** `aether agent add maria --workspace /custom/path/maria` is executed
- **THEN** the workspace SHALL be created at `/custom/path/maria` and `config.json` SHALL record that path

#### Scenario: Non-interactive mode

- **WHEN** `aether agent add maria --non-interactive` is executed
- **THEN** all files SHALL be created with default templates without prompting the user

#### Scenario: Duplicate agent name rejected

- **WHEN** `aether agent add maria` is executed but an agent named `maria` already exists in `config.json`
- **THEN** the command SHALL exit with a non-zero code and print an error: "Agent 'maria' already exists. Use 'aether agent delete maria' first."

#### Scenario: Interactive mode prompts for model

- **WHEN** `aether agent add maria` is executed without `--non-interactive`
- **THEN** the user SHALL be prompted to select a default model from a list of configured providers

### Requirement: Agent list command enumerates all agents

`aether agent list` SHALL print a table of all registered agents from `~/.aether/config.json` with name, workspace path, primary model, channel binding count, and heartbeat interval.

#### Scenario: List with multiple agents

- **WHEN** `aether agent list` is executed with agents `maria`, `aria`, and `erza` configured
- **THEN** output SHALL include one row per agent showing name, workspace path, model, binding count

#### Scenario: JSON output

- **WHEN** `aether agent list --json` is executed
- **THEN** output SHALL be a JSON array of agent entries matching the `config.json` schema

#### Scenario: Empty agent list

- **WHEN** `aether agent list` is executed with no agents configured
- **THEN** output SHALL print "No agents configured. Use 'aether agent add <name>' to create one."

### Requirement: Agent delete command removes agent

`aether agent delete <name>` SHALL remove the agent entry from `~/.aether/config.json`. With `--prune-workspace`, it SHALL also delete the workspace directory. With `--force`, it SHALL skip confirmation prompts.

#### Scenario: Agent removed from config

- **WHEN** `aether agent delete maria` is executed
- **THEN** `config.json` SHALL no longer contain `agents.maria`

#### Scenario: Workspace pruned

- **WHEN** `aether agent delete maria --prune-workspace --force` is executed
- **THEN** `~/.aether/workspaces/maria/` SHALL be deleted

#### Scenario: Workspace preserved by default

- **WHEN** `aether agent delete maria` is executed without `--prune-workspace`
- **THEN** `~/.aether/workspaces/maria/` SHALL remain on disk

#### Scenario: Nonexistent agent

- **WHEN** `aether agent delete nonexistent` is executed
- **THEN** the command SHALL exit with non-zero code and print: "Agent 'nonexistent' not found."

#### Scenario: Confirmation prompt

- **WHEN** `aether agent delete maria` is executed without `--force`
- **THEN** the user SHALL be prompted to confirm deletion before the agent is removed

### Requirement: Agent set-identity updates display metadata

`aether agent set-identity <name>` SHALL update the agent's display metadata (display name, emoji, avatar path) in `~/.aether/config.json` without touching the workspace files.

#### Scenario: Display name updated

- **WHEN** `aether agent set-identity maria --display-name "Maria 🌸"` is executed
- **THEN** `config.json` `agents.maria.displayName` SHALL be `"Maria 🌸"`

#### Scenario: Emoji updated

- **WHEN** `aether agent set-identity maria --emoji "🌟"` is executed
- **THEN** `config.json` `agents.maria.emoji` SHALL be `"🌟"`

#### Scenario: No changes without flags

- **WHEN** `aether agent set-identity maria` is executed without any flags
- **THEN** the command SHALL print current identity values and exit (read-only mode)

### Requirement: Template files contain valid defaults

Agent workspace scaffolded files SHALL contain valid placeholder content — not empty files. SOUL.md SHALL describe the agent voice pattern. IDENTITY.md SHALL include the exposure classification template. HEARTBEAT.md SHALL contain a valid task list with HEARTBEAT_OK marker.

#### Scenario: SOUL.md has voice template

- **WHEN** `aether agent add newagent` completes
- **THEN** `workspaces/newagent/SOUL.md` SHALL contain sections for Tone, Address, Rules, and Memory

#### Scenario: HEARTBEAT.md has task list

- **WHEN** `aether agent add newagent` completes
- **THEN** `workspaces/newagent/HEARTBEAT.md` SHALL contain a markdown task list with at least one item: "Check TASK_INBOX.md for pending tasks"

#### Scenario: AGENTS_GUARD.md has security sections

- **WHEN** `aether agent add newagent` completes
- **THEN** `workspaces/newagent/AGENTS_GUARD.md` SHALL contain sections for Configuration Isolation, Red Lines, Anti-Hang, and State Recovery
