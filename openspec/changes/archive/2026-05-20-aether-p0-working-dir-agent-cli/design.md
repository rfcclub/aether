## Context

Aether currently runs from the repository root — agent profiles live at `agents/<name>/` relative to the working directory, database at `store/aether.db`, and configuration is a flat `appsettings.json` with env var overrides. There is no install-time directory, no CLI beyond the harness (`dotnet run -- --prompt`), and no per-agent isolation of credentials or model preferences.

OpenClaw demonstrates the mature pattern: `~/.openclaw/` as the state root containing agents, workspaces, store, cron, logs, backups, and identity. The `openclaw agents` CLI group manages agent lifecycle. Configuration merges from framework defaults through user overrides to agent-specific settings.

This design defines how Aether adopts that pattern while preserving its existing DI architecture, .NET 9 host model, and provider abstractions.

**Constraints:**
- Must remain a .NET 9 single-process application
- Existing `appsettings.json` and `AETHER_*` env var patterns must continue to work
- Breaking changes to `IAgentProfile`, `AgentConfig`, and `AetherSoul` contracts must be minimal
- Maria agent workspace must be migratable from `agents/maria/` to `~/.aether/workspaces/maria/`
- No external service dependencies — everything is filesystem + SQLite

## Goals / Non-Goals

**Goals:**
- Auto-create `~/.aether/` with full directory tree on first Aether execution
- Five-layer configuration merge: framework defaults → global user config → agent config → env vars → CLI flags
- `aether agent` CLI via `System.CommandLine` for lifecycle management (add, list, delete, set-identity, bind, unbind)
- Agent workspace scaffolding with all personality files templated from defaults
- Per-agent provider authentication isolation (auth-state.json, auth-profiles.json, models.json)
- Channel-to-agent binding persisted in `~/.aether/config.json` and resolved at message routing time
- Interactive first-run wizard detecting missing `~/.aether/config.json`
- Backward-compatible: existing harness mode (`--prompt`) continues to work with reasonable defaults

**Non-Goals:**
- Plugin/extension marketplace — plugin loading hooks are defined but extension discovery is P3
- Multi-node / distributed agent execution — single-host only
- Web-based admin UI — CLI + TUI only
- Agent-to-agent communication protocol (ACP) — routing is channel-to-agent, not agent-to-agent
- Backup/restore commands — directory structure defined but backup CLI is P2
- Cron scheduler CLI — jobs.json format defined but cron CLI is P2
- Session browser CLI — session data stored but browse/replay/export CLI is P3

## Decisions

### Decision 1: Working directory at `~/.aether/`

**Choice:** `~/.aether/` as the single state root, mirroring OpenClaw's `~/.openclaw/`.

**Alternatives considered:**
- `$XDG_DATA_HOME/aether/` — more standards-compliant but OpenClaw's flat `~/.openclaw/` pattern is simpler and Thoor already expects it
- `<repo>/state/` — couples runtime state to the repo checkout; breaks when repo is updated independently
- `$AETHER_HOME` env var with `~/.aether/` default — adds indirection for a use case that doesn't exist yet

**Rationale:** OpenClaw's `~/.openclaw/` is battle-tested across 6+ months of daily use with 13 agents. The directory structure, file naming conventions, and config format are proven. Deviating would create unnecessary mental overhead for the same operator.

### Decision 2: `System.CommandLine` for CLI parsing

**Choice:** `System.CommandLine` NuGet package for the `aether` CLI entry point.

**Alternatives considered:**
- `Spectre.Console.Cli` — richer rendering but opinionated command app model; overkill for initial CLI
- Manual `args[]` parsing — current approach in `Program.cs`; doesn't scale past 5 flags
- `Cocona` — minimal but third-party bus factor

**Rationale:** `System.CommandLine` is Microsoft-maintained, integrates naturally with .NET Generic Host, and supports the exact pattern needed: root command with subcommands (`agent add`, `agent list`, etc.). We use `Spectre.Console` separately for the interactive wizard prompts (rich text, selection prompts) but not for command routing.

### Decision 3: Configuration merge order

**Choice:** Five layers merged in order (later wins):

1. **Framework defaults** — `appsettings.json` in the Aether assembly directory
2. **Global user config** — `~/.aether/config.json`
3. **Agent-specific config** — `<workspace>/.aether.json`
4. **Environment variables** — `AETHER_*` with `__` nesting separator
5. **CLI flags** — `--agent.name`, `--model`, etc.

**Alternatives considered:**
- Three layers (framework → env → CLI) — current approach; no agent isolation, no user-local overrides
- .NET's `IConfiguration` chain natively — works for strings but doesn't give typed config objects

**Rationale:** The five-layer model maps directly to OpenClaw's config resolution. Layer 3 (agent-specific) is the key addition — it enables per-agent model preferences, heartbeat intervals, and tool permissions without touching global config. The `ConfigLoader` service produces strongly-typed `AetherAppConfig`, `AgentConfig`, and per-agent `AgentAuthConfig` objects.

### Decision 4: Agent workspace as directory, not database row

**Choice:** Agent personality lives as files in `~/.aether/workspaces/<name>/` — same format as the current `agents/<name>/` directory. Agent metadata (name, bindings, model) lives in `~/.aether/config.json`.

**Alternatives considered:**
- SQLite `agents` table for all agent state — more queryable but loses file-based interoperability; can't `cat SOUL.md` or edit with any editor
- Hybrid: metadata in SQLite, personality files on disk — adds sync complexity for no benefit

**Rationale:** File-based agent personality is the OpenClaw pattern and already works in Aether via `AgentProfile.LoadFileAsync()`. `config.json` holds the registry of agents with their workspace path, channel bindings, and overrides. The workspace directory is the source of truth for personality.

### Decision 5: Per-agent auth profiles via JSON files

**Choice:** Each agent gets `~/.aether/agents/<name>/agent/auth-state.json` (active provider+model), `auth-profiles.json` (credentials per provider), and `models.json` (model allowlist with overrides). Loaded at agent resolution time and merged into `ILLMProvider` configuration.

**Alternatives considered:**
- Single global `~/.aether/auth.json` — no isolation between agents
- Environment variables per agent (`AETHER_MARIA_llm__api_key`) — explodes env var namespace with 13+ agents

**Rationale:** JSON files match OpenClaw's pattern exactly. The `AgentAuthConfig` is loaded by `ConfigLoader` when resolving an agent and injected into the provider for that agent's `AetherSoul` instance. Credentials never leave the `~/.aether/agents/<name>/agent/` directory.

### Decision 6: First-run wizard as interactive console flow

**Choice:** `Spectre.Console` for the interactive wizard. Detects missing `~/.aether/config.json` → prompts for provider, API key, agent name, optional Telegram token → writes config and scaffolds first workspace.

**Alternatives considered:**
- Web-based setup wizard — overkill for a CLI framework
- Non-interactive only (`aether init --non-interactive --provider ...`) — poor DX for first-time users
- Skip wizard entirely, require manual config file creation — OpenClaw has a wizard for a reason

**Rationale:** The wizard runs once. `--non-interactive` flag skips it for automated deployments. The wizard writes `~/.aether/config.json` with `wizard.lastRunAt` and `wizard.lastRunVersion` metadata matching OpenClaw's pattern.

### Decision 7: ConfigLoader as DI singleton, not static

**Choice:** `ConfigLoader` registered as `Singleton` in the DI container, wrapping `IConfiguration` from the host builder.

**Alternatives considered:**
- Static `ConfigLoader.Load()` — simpler but untestable, can't mock for unit tests
- Extension methods on `IConfiguration` — scatters config logic across codebase

**Rationale:** DI singleton makes it injectable into `AgentProfile`, `ProviderRouter`, `MessageRouter`, and CLI command handlers. Testable with `Mock<IConfiguration>`. Single load at startup, cached for lifetime.

### Decision 8: Channel binding as JSON array in agent config

**Choice:** Channel bindings stored as `bindings: ["telegram:chat123", "discord:guild456"]` in the agent's entry in `~/.aether/config.json`.

**Alternatives considered:**
- Separate `bindings.json` per agent — more files, no benefit
- SQLite `agent_bindings` table — requires DB connection for CLI read operations

**Rationale:** Bindings are small (< 10 per agent), human-editable, and read at startup + on CLI `list`. `config.json` is already loaded into memory. The `MessageRouter` resolves agent from inbound `channel_type:chat_id` by scanning all agents' bindings at route time (cached after first scan).

## Risks / Trade-offs

- **[Risk] `~/.aether/` grows unboundedly** — workspaces accumulate memory logs, sessions accumulate JSONL trajectories. → **Mitigation**: Directory structure defines expected sizes per subdirectory. `AetherInitializationService` logs a warning if `store/` exceeds 500MB. Compaction is P2.
- **[Risk] Config merge conflicts** — agent `.aether.json` may override global settings in unexpected ways. → **Mitigation**: `aether config show --effective` command (P2) prints the fully merged config for debugging.
- **[Risk] Migration from `agents/<name>/` to `~/.aether/workspaces/<name>/` breaks Maria** — existing `agents/maria/` directory is gitignored but still on disk. → **Mitigation**: `aether agent add maria` creates the workspace directory; the user copies files manually. The framework falls back to `<cwd>/agents/<name>/` if `~/.aether/workspaces/<name>/` doesn't exist (backward compat, deprecated warning).
- **[Risk] `System.CommandLine` adds ~500KB to publish output** — minor dependency weight. → **Trade-off accepted**: CLI parsing is table-stakes for a framework.
- **[Risk] Auth JSON files contain API keys** — plaintext credentials on disk. → **Mitigation**: `~/.aether/agents/<name>/agent/` directories get `chmod 700`. Documentation warns not to share the directory. Future: optional keyring integration (P3).

## Directory Contract

```
~/.aether/
├── config.json                  # Global config (providers, agent registry, wizard metadata)
├── identity/
│   └── device.json              # { deviceId, createdAt, version }
├── agents/
│   └── <name>/
│       ├── agent/
│       │   ├── auth-state.json       # { activeProvider, activeModel }
│       │   ├── auth-profiles.json    # { "openrouter": { mode, apiKey }, ... }
│       │   └── models.json           # { primary, fallbacks[], overrides: {} }
│       └── sessions/
│           └── <uuid>.jsonl          # Session trajectory
├── workspaces/
│   └── <name>/
│       ├── .aether.json              # Agent-specific config overrides
│       ├── SOUL.md                   # Voice and personality
│       ├── USER.md                   # Human description
│       ├── IDENTITY.md               # Self-model and boundaries
│       ├── MEMORY.md                 # Long-term memory
│       ├── HEARTBEAT.md              # Periodic task list
│       ├── AGENTS_GUARD.md           # Red lines and anti-hang rules
│       ├── DREAMS.md                 # Dream diary (FEOFALLS)
│       ├── INTROSPECTION.md          # Episodic log (FEOFALLS)
│       ├── TASK_INBOX.md             # Incoming task queue
│       ├── TASK_REPORT.md            # Completed task reports
│       ├── memory/                   # Daily episodic logs (YYYY-MM-DD.md)
│       ├── plans/                    # Agent-generated plans
│       └── research/                 # Agent research notes
├── store/
│   ├── aether.db                    # Main SQLite database
│   └── memory.db                    # FTS5 memory index
├── cron/
│   └── jobs.json                    # Scheduled task definitions
├── logs/
│   └── aether-YYYY-MM-DD.log        # Daily log files
└── backups/                          # Backup archives (P2)
```

## ConfigLoader Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      ConfigLoader                            │
│                                                              │
│  LoadAsync() →                                              │
│    1. Load appsettings.json from assembly dir                │
│    2. Load ~/.aether/config.json (if exists)                │
│    3. Load <workspace>/.aether.json (if agent specified)    │
│    4. Overlay AETHER_* env vars                             │
│    5. Overlay CLI flags                                     │
│    6. Return AetherAppConfig                                │
│                                                              │
│  AetherAppConfig                                             │
│  ├── Providers: ProviderConfig                              │
│  ├── AgentDefaults: AgentDefaultsConfig                     │
│  ├── ChannelDefaults: ChannelConfig                         │
│  ├── Sandbox: SandboxConfig                                 │
│  └── Agents: Dictionary<string, AgentEntryConfig>           │
│       └── AgentEntryConfig                                  │
│           ├── Name: string                                  │
│           ├── Workspace: string           (path)            │
│           ├── Model: AgentModelConfig                       │
│           │   ├── Primary: string                           │
│           │   └── Fallbacks: string[]                       │
│           ├── Bindings: string[]          (channel:chatId)  │
│           ├── HeartbeatInterval: int?     (minutes)          │
│           └── Enabled: bool                                 │
└─────────────────────────────────────────────────────────────┘
```

## CLI Command Tree

```
aether
├── agent
│   ├── add <name> [--workspace <path>] [--model <id>] [--non-interactive]
│   ├── list [--json]
│   ├── delete <name> [--prune-workspace] [--force]
│   ├── set-identity <name> [--display-name <name>] [--emoji <emoji>]
│   ├── bind <name> --channel <type:chatId>
│   └── unbind <name> --channel <type:chatId>
├── serve [--port <n>] [--agent <name>]
├── tui [--agent <name>]
├── run --prompt <text> [--agent <name>] [--group <name>] [--model <id>]
└── config
    └── show [--effective] [--agent <name>]  (P2)
```

## Migration Plan

1. `AetherInitializationService` gains `WorkingDirectoryInitializer` call before existing DB init
2. Working directory init is idempotent — `Directory.CreateDirectory()` for each path, never overwrites existing files
3. For backward compat: `AgentProfile` checks `~/.aether/workspaces/<name>/` first, falls back to `<cwd>/agents/<name>/` with deprecation warning
4. Maria migration: `aether agent add maria` creates `~/.aether/workspaces/maria/`, user copies existing files from `agents/maria/`
5. Old `agents/` directory remains gitignored and ignored by Aether once migration is done
6. No automated migration — explicit copy preserves user control over what moves

## Open Questions

- **Should `aether agent add` accept a `--from <path>` flag to import an existing agent workspace?** → Defer to P1.5 — manual copy is sufficient for Maria migration.
- **Should `AETHER_HOME` env var override `~/.aether/`?** → Yes, trivial to add. Include in initial implementation as `Environment.GetEnvironmentVariable("AETHER_HOME") ?? Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".aether")`.
- **Should agent workspaces support symlinks?** → Not explicitly. If the filesystem supports it, it works. Aether won't resolve or validate symlinks.
