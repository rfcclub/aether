## Why

Aether is a common agent framework but has no runtime home directory, no agent lifecycle CLI, and no per-agent configuration isolation. Every Aether instance today runs from the repo directory with hardcoded paths and global config — it cannot function as a deployed framework. OpenClaw demonstrates the proven pattern: `~/.openclaw/` as the state root, `agents add/list/delete` as the management surface, and layered config that separates framework defaults from user overrides from agent-specific settings. Aether needs this foundation before any other feature can land.

## What Changes

- **New**: `~/.aether/` working directory auto-created on first run with full directory tree (config, identity, agents, workspaces, store, cron, logs, backups)
- **New**: `aether agent` CLI command group — `add`, `list`, `delete`, `set-identity` subcommands
- **New**: Agent workspace scaffolding on `agent add` — writes SOUL.md, IDENTITY.md, MEMORY.md, HEARTBEAT.md, AGENTS_GUARD.md, DREAMS.md, INTROSPECTION.md, TASK_INBOX.md, TASK_REPORT.md, USER.md, memory/ directory, .aether.json
- **New**: Configuration hierarchy — framework `appsettings.json` → `~/.aether/config.json` (global) → `<workspace>/.aether.json` (agent) → `AETHER_*` env vars → CLI flags, merged at startup
- **New**: `ConfigLoader` service that loads and merges all config layers
- **New**: Per-agent auth profiles — `~/.aether/agents/<name>/agent/auth-state.json`, `auth-profiles.json`, `models.json`
- **New**: Channel binding CLI — `aether agent bind <name> --channel <type:id>` and `unbind`, persisted in agent config
- **New**: First-run wizard — detects missing `~/.aether/config.json`, interactive prompt for provider, API key, agent name, optional Telegram token
- **Modified**: `Program.cs` entry point — detects `~/.aether/`, runs wizard or normal startup, wires `ConfigLoader`
- **Modified**: `AgentConfig` — sources from merged config layers instead of hardcoded defaults only
- **Modified**: `AgentProfile` — reads from `~/.aether/workspaces/<name>/` instead of repo-relative `agents/<name>/`
- **Modified**: `ProviderRouter` — accepts per-agent provider overrides from auth profiles
- **Modified**: `MessageRouter` — resolves agent from channel bindings in `~/.aether/config.json`

## Capabilities

### New Capabilities

- `working-directory`: Auto-creation of `~/.aether/` directory tree on first run, device identity file, directory layout contract
- `agent-cli`: CLI commands for agent lifecycle — add (scaffold workspace), list (enumerate configured agents), delete (remove with optional workspace prune), set-identity (update name/emoji/avatar)
- `config-hierarchy`: Five-layer configuration merge (framework → global → agent → env → CLI), `ConfigLoader` service, typed config objects
- `channel-binding`: CLI to bind/unbind agents to Telegram/Discord/WebSocket channels, persisted routing rules, inbound message agent resolution
- `first-run-wizard`: Interactive first-run detection and setup, provider selection, API key input, initial agent creation
- `agent-auth-profiles`: Per-agent provider credentials, model preferences, fallback chains isolated from other agents

### Modified Capabilities

- `agent-core`: Agent profile resolution now reads from `~/.aether/workspaces/<name>/` instead of repo-relative `agents/<name>/`. AgentConfig sourced from merged config layers. Startup file defaults remain the same contract.
- `multi-agent`: Expanded with channel binding persistence, per-agent auth isolation, agent lifecycle CLI. Existing multi-agent routing spec extended with binding-based agent resolution.

## Impact

- **Code**: `Program.cs` (entry point restructure), new `Aether.Cli/` namespace for CLI commands, new `Aether.Config/` namespace for config loading, new `Aether.WorkingDirectory/` namespace for directory init, modified `Agents/AgentProfile.cs`, `Agents/AgentConfig`, `Providers/ProviderRouter`, `Routing/MessageRouter`
- **Dependencies**: Add `System.CommandLine` or `Spectre.Console.Cli` NuGet package for CLI parsing. Add `Spectre.Console` for interactive wizard prompts.
- **Filesystem**: Creates `~/.aether/` with 15+ subdirectories on first run. Writes `config.json`, agent workspace files, auth state files.
- **Breaking**: Agent workspaces move from `<repo>/agents/<name>/` to `~/.aether/workspaces/<name>/`. Existing Maria workspace must be migrated with `aether agent add maria` then copy files. Provider config changes from flat `appsettings.json` to layered merge — existing env var patterns preserved (`AETHER_*` prefix).
- **Existing specs modified**: `agent-core` (profile resolution path), `multi-agent` (binding + lifecycle + auth isolation)
