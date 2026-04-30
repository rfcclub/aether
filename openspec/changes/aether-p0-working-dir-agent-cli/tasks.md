## 1. Project Setup and Dependencies

- [x] 1.1 Add `System.CommandLine` NuGet package to `src/Aether/Aether.csproj` for CLI parsing
- [x] 1.2 Add `Spectre.Console` NuGet package to `src/Aether/Aether.csproj` for interactive wizard prompts
- [x] 1.3 Create new namespace directories: `src/Aether/Cli/`, `src/Aether/Config/`, `src/Aether/WorkingDirectory/`, `src/Aether/Workspace/`
- [x] 1.4 Create `src/Aether/Templates/` directory for workspace scaffold template content

## 2. Working Directory Initialization

- [x] 2.1 Create `WorkingDirectory/WorkingDirectoryInitializer.cs` implementing `IHostedService`
- [x] 2.2 Implement `InitializeAsync()` that checks for `~/.aether/` (or `$AETHER_HOME`), creates full directory tree if missing
- [x] 2.3 Implement directory tree creation: `identity/`, `agents/`, `workspaces/`, `store/`, `cron/`, `logs/`, `backups/`
- [x] 2.4 Create `identity/device.json` with UUID v4 device ID, ISO 8601 timestamp, and Aether version on first run
- [x] 2.5 Implement idempotency — no overwrite of existing files or directories
- [x] 2.6 Register `WorkingDirectoryInitializer` in DI before `AetherInitializationService`
- [x] 2.7 Update `AetherInitializationService` to accept `WorkingDirectoryInitializer` dependency or ensure ordering via host configuration

## 3. Configuration Hierarchy

- [x] 3.1 Create `Config/AetherAppConfig.cs` — top-level config record: `Providers`, `AgentDefaults`, `ChannelDefaults`, `Sandbox`, `Agents` dict
- [x] 3.2 Create `Config/AgentEntryConfig.cs` — per-agent config: `Name`, `Workspace`, `Model` (with `Primary` and `Fallbacks`), `Bindings`, `HeartbeatInterval`, `Enabled`, `DisplayName`, `Emoji`
- [x] 3.3 Create `Config/AgentModelConfig.cs` — `Primary` (string), `Fallbacks` (string[]), `Overrides` (dictionary)
- [x] 3.4 Create `Config/ConfigLoader.cs` — singleton service with `LoadAsync()` that merges 5 layers
- [x] 3.5 Implement Layer 1: Load `appsettings.json` from assembly directory
- [x] 3.6 Implement Layer 2: Merge `~/.aether/config.json` on top
- [x] 3.7 Implement Layer 3: Merge `<workspace>/.aether.json` on top (when agent specified)
- [x] 3.8 Implement Layer 4: Overlay `AETHER_*` environment variables with `__` nesting separator and single-underscore fallback
- [x] 3.9 Implement Layer 5: Overlay CLI flags (`--model`, `--agent.name`, etc.)
- [x] 3.10 Implement config validation: warn if no API key, throw if no provider configured
- [x] 3.11 Implement `GetAgentConfig(string name)` returning `AgentEntryConfig?`
- [x] 3.12 Register `ConfigLoader` as singleton in DI (wraps `IConfiguration`)
- [x] 3.13 Write `~/.aether/config.json` with `meta.lastTouchedVersion`, `wizard.lastRunAt` on first creation

## 4. Agent Workspace Scaffolding

- [x] 4.1 Create `Workspace/AgentWorkspaceScaffolder.cs` with `ScaffoldAsync(string name, string workspacePath, bool interactive)`
- [x] 4.2 Create template content for each scaffolded file in `Templates/` directory or as embedded string constants:
  - `SOUL.md` template: Tone, Address, Rules, Memory sections
  - `USER.md` template: Name, What to call them, Timezone, Notes sections
  - `IDENTITY.md` template: Name, Creature, Vibe, Emoji, Exposure Classification, Conflict Engine stubs
  - `MEMORY.md` template: User, Agent Context, Multi-Agent Ecosystem sections
  - `HEARTBEAT.md` template: Task list with "Check TASK_INBOX.md" item and `HEARTBEAT_OK` marker
  - `AGENTS_GUARD.md` template: Configuration Isolation, Red Lines, Anti-Hang, State Recovery sections
  - `DREAMS.md` template: Empty with header
  - `INTROSPECTION.md` template: Empty with header
  - `TASK_INBOX.md` template: Empty with header
  - `TASK_REPORT.md` template: Empty with header
  - `.aether.json` template: `{ "model": { "primary": null, "fallbacks": [] }, "heartbeat": { "intervalMinutes": 60 } }`
- [x] 4.3 Create `memory/` subdirectory inside workspace
- [x] 4.4 Implement interactive prompt for model selection during scaffolding (when not `--non-interactive`)
- [x] 4.5 Implement file existence check — never overwrite existing workspace files
- [x] 4.6 Register `AgentWorkspaceScaffolder` as singleton in DI

## 5. Per-Agent Auth Profiles

- [x] 5.1 Create `Config/AgentAuthProfiles.cs` — reads/writes `auth-state.json`, `auth-profiles.json`, `models.json`
- [x] 5.2 Implement `CreateAuthDirectoryAsync(string agentName)` — creates `~/.aether/agents/<name>/agent/` with three JSON files
- [x] 5.3 Implement `auth-state.json`: `{ "activeProvider": null, "activeModel": null }` default
- [x] 5.4 Implement `auth-profiles.json`: provider-to-credentials mapping with `mode`, `apiKey`, `email`
- [x] 5.5 Implement `models.json`: `primary`, `fallbacks[]`, `overrides{}` for per-model parameter tuning
- [x] 5.6 Implement `LoadAuthProfilesAsync(string agentName)` returning typed `AgentAuthConfig`
- [x] 5.7 Implement Linux `chmod 700` on `~/.aether/agents/<name>/agent/` directory (Windows: no-op)
- [x] 5.8 Integrate auth profile loading into `ConfigLoader` — agent auth overrides global provider config

## 6. CLI Command System

- [x] 6.1 Create `Cli/AetherCli.cs` — `System.CommandLine` root command setup with subcommands
- [x] 6.2 Modify `Program.cs` entry point: detect if first arg matches a CLI command, dispatch to `AetherCli`, else proceed to harness/serve/tui
- [x] 6.3 Implement `aether agent add <name>` command with flags: `--workspace`, `--model`, `--non-interactive`
- [x] 6.4 Implement `aether agent list` command with `--json` flag for machine-readable output
- [x] 6.5 Implement `aether agent delete <name>` command with flags: `--prune-workspace`, `--force`
- [x] 6.6 Implement `aether agent set-identity <name>` command with flags: `--display-name`, `--emoji`, `--avatar`
- [x] 6.7 Implement `aether agent bind <name> --channel <type:chatId>` command
- [x] 6.8 Implement `aether agent unbind <name> --channel <type:chatId>` command
- [x] 6.9 Implement `aether agent bind <name>` (no `--channel` flag) to list current bindings
- [x] 6.10 Wire CLI commands to `ConfigLoader`, `AgentWorkspaceScaffolder`, and `AgentAuthProfiles` via DI

## 7. First-Run Wizard

- [x] 7.1 Create `Cli/FirstRunWizard.cs` using `Spectre.Console` for interactive prompts
- [x] 7.2 Implement first-run detection: check `~/.aether/config.json` existence after working directory init
- [x] 7.3 Implement `--non-interactive` mode: create minimal `config.json` with framework defaults, skip all prompts
- [ ] 7.4 Implement provider selection prompt (OpenRouter / Anthropic / Fireworks / Other)
- [ ] 7.5 Implement API key prompt (masked input with `Spectre.Console`)
- [ ] 7.6 Implement agent name prompt with default value "default"
- [ ] 7.7 Implement optional Telegram setup prompt (y/n → bot token input)
- [x] 7.8 Write `config.json` with wizard metadata: `wizard.lastRunAt`, `wizard.lastRunVersion`, `wizard.lastRunCommand`
- [ ] 7.9 Print completion summary message with next-step instructions
- [x] 7.10 Integrate wizard into `Program.cs`: runs before harness/serve/tui if `config.json` missing

## 8. Channel Binding Resolution

- [ ] 8.1 Update `MessageRouter` to scan all agents' `bindings` arrays for `<channel_type>:<chat_id>` match
- [ ] 8.2 Implement binding cache with invalidation on `config.json` file change (check `LastWriteTimeUtc`)
- [ ] 8.3 Implement fallback: no binding match → default agent (first `enabled: true` or agent named "default")
- [ ] 8.4 Implement legacy `gateway.agents.<source>` key support with deprecation warning log
- [ ] 8.5 Update `ChannelMessageProcessor` to use resolved agent name from `MessageRouter`

## 9. Backward Compatibility and Migration

- [ ] 9.1 Update `AgentProfile` constructor to accept `ConfigLoader` and resolve workspace path from `~/.aether/workspaces/<name>/`
- [ ] 9.2 Implement fallback: if `~/.aether/workspaces/<name>/` doesn't exist, try `<cwd>/agents/<name>/` with deprecation warning
- [ ] 9.3 Throw `DirectoryNotFoundException` if neither path exists (with clear message)
- [ ] 9.4 Update `AgentConfig` to source defaults from `ConfigLoader` merged config instead of hardcoded values
- [ ] 9.5 Update `Program.cs` harness mode (`--prompt`) to work without `~/.aether/` — use repo-relative paths as fallback
- [ ] 9.6 Ensure existing `appsettings.json` and `AETHER_*` env var patterns continue to work unchanged
- [ ] 9.7 Verify Maria migration path: `aether agent add maria` + manual file copy from `agents/maria/`
- [ ] 9.8 Ensure `agents/` directory remains in `.gitignore` (already configured)

## 10. Provider Router Integration

- [ ] 10.1 Update `ProviderRouter` to accept per-agent `AgentAuthConfig` for credential override
- [ ] 10.2 Implement provider credential resolution: agent auth → global config → env vars
- [ ] 10.3 Implement model fallback chain from agent's `models.json` in `ProviderRouter`
- [ ] 10.4 Implement per-model parameter overrides (`maxTokens`, `temperature`, etc.) from `models.json`

## 11. Tests

- [ ] 11.1 Add `WorkingDirectoryInitializerTests.cs` — test first-run creation, idempotency, custom `AETHER_HOME`, device.json content
- [ ] 11.2 Add `ConfigLoaderTests.cs` — test 5-layer merge order, env var mapping, validation warnings/errors, agent config resolution
- [ ] 11.3 Add `AgentWorkspaceScaffolderTests.cs` — test file creation, template content, non-overwrite, interactive/non-interactive modes
- [ ] 11.4 Add `AgentAuthProfilesTests.cs` — test auth file creation, credential loading, chmod on Linux, global fallback
- [ ] 11.5 Add `CliCommandTests.cs` — test `agent add/list/delete/set-identity/bind/unbind` command execution and output
- [ ] 11.6 Add `FirstRunWizardTests.cs` — test detection, non-interactive mode, config.json metadata
- [ ] 11.7 Add `ChannelBindingResolutionTests.cs` — test binding match, fallback, cache invalidation, legacy config
- [ ] 11.8 Add `BackwardCompatibilityTests.cs` — test repo-relative fallback, deprecation warnings, harness mode without `~/.aether/`
- [ ] 11.9 Add `AgentProfileTests.cs` — updated tests for `~/.aether/` resolution and fallback paths
- [ ] 11.10 Update existing tests that depend on `AgentConfig` or `AgentProfile` to work with new config hierarchy
- [ ] 11.11 Run full test suite (`dotnet test`): all existing 112+ tests pass + new tests pass, 0 failures

## 12. Build and Release

- [ ] 12.1 Update `src/Aether/Aether.csproj` with new NuGet package references
- [ ] 12.2 Fix any compilation errors from new namespaces and type references
- [ ] 12.3 Build solution: `dotnet build` with zero errors
- [ ] 12.4 Publish framework: `dotnet publish -c Release -o releases/latest`
- [ ] 12.5 Run smoke test: `./releases/latest/Aether --prompt "hello"` works with env var API key
- [ ] 12.6 Run `aether agent add test-agent --non-interactive` and verify workspace scaffolded

## 13. Documentation and Commit

- [ ] 13.1 Update `SETUP.md` with `~/.aether/` working directory documentation
- [ ] 13.2 Update `ARCHITECTURE.md` with ConfigLoader, WorkingDirectory, and CLI architecture sections
- [ ] 13.3 Add `aether agent --help` examples to `SETUP.md`
- [ ] 13.4 Commit all changes with message: `feat: add working directory, agent CLI, config hierarchy, channel binding, first-run wizard`
- [ ] 13.5 Push to remote `https://github.com/rfcclub/aether.git`
