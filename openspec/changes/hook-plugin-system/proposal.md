## Why

Aether's extension model is implicit and hardcoded — access control, parameter validation, tool dispatch, and message routing are woven directly into `ChannelMessageProcessor` and `AetherSoul`. Adding a new capability requires modifying source code. Users cannot intercept, transform, or block the agent pipeline without forking Aether. A formal hook and plugin system turns Aether from a closed runtime into an extensible platform — agents can be customized with plugins that observe, transform, or control every stage of the message lifecycle, without touching core source.

## What Changes

- Add `IHook` interface with 14 hook points spanning the full agent lifecycle: message pipeline, LLM calls, tool execution, session management, memory operations, and agent lifecycle events
- Add `HookEngine` that executes hooks in priority order with short-circuit semantics (Pre hooks block, Post hooks observe)
- Add **plugin system**: self-contained directories with `plugin.json` manifest, optional compiled assembly, bundled tools/skills/channels/cron
- Add **capability-based service access**: plugins declare required DI services in manifest; `PluginPermissionGate` enforces at runtime
- Add 6 extension interfaces: `IHook`, `IToolImplementation` (existing, unchanged), `IChannel` (existing, unchanged), `ISkillProvider`, `ICronTaskProvider`, `IPluginLifecycle`
- Add `IsolatedPluginLoadContext` per plugin with collectible assemblies for unload/reload
- Add `aether plugin` CLI: install, list, enable, disable, uninstall
- Add per-agent plugin configuration in `.aether.json`
- Weave hooks into `AetherSoul`, `ChannelMessageProcessor`, `SessionManager`, `MemorySystem`, `AgentHeartbeatService`, `Program.cs`

## Capabilities

### New Capabilities
- `hook-system`: IHook interface, 14 hook points (OnMessageReceived, OnMessageRouted, OnMessageSent, PreLlmCall, PostLlmCall, PreToolUse, PostToolUse, OnSessionStart, OnSessionCompact, OnSessionEnd, OnMemoryWrite, OnMemoryPromote, OnAgentStart, OnAgentStop, OnHeartbeatTick), HookEngine with priority-ordered execution and exception isolation
- `plugin-system`: Plugin manifest (plugin.json), PluginLoader with dependency resolution, IsolatedPluginLoadContext per plugin, 6 extension interfaces, capability-based service access via PluginPermissionGate, plugin lifecycle management (install/enable/disable/uninstall)
- `plugin-config`: Per-agent plugin enable/disable, configuration overrides, hook priority overrides in `.aether.json`, Plugin CLI (aether plugin install/list/show/enable/disable/uninstall)

### Modified Capabilities
- `aether-soul-tool-loop`: Hook integration — fire PreLlmCall/PostLlmCall around LLM calls, PreToolUse/PostToolUse around tool execution. PreLlmCall can modify system prompt and escalate provider. PreToolUse can deny or transform tool arguments. PostToolUse can transform tool results.
- `gateway`: Hook integration — fire OnMessageReceived before access check, OnMessageRouted after agent resolution, OnMessageSent before channel delivery. OnMessageReceived can drop or transform messages. OnMessageSent can suppress or transform output.
- `agent-core`: Hook integration — register HookEngine, PluginLoader, and PluginPermissionGate in DI; fire OnAgentStart after boot, OnAgentStop on shutdown
- `memory-system`: Hook integration — fire OnMemoryWrite before/after memory writes to any layer (ephemeral/working/durable), OnMemoryPromote on promotion candidates

## Impact

- **New source files**: `Plugins/IHook.cs`, `Plugins/HookEngine.cs`, `Plugins/HookContexts.cs`, `Plugins/HookResult.cs`, `Plugins/PluginManifest.cs`, `Plugins/PluginLoader.cs`, `Plugins/IsolatedPluginLoadContext.cs`, `Plugins/PluginPermissionGate.cs`, `Plugins/IPluginLifecycle.cs`, `Cli/PluginCli.cs` (~10 files)
- **Modified files**: `Agent/AetherSoul.cs` (weave PreLlmCall, PostLlmCall, PreToolUse, PostToolUse), `Channels/ChannelMessageProcessor.cs` (weave OnMessageReceived, OnMessageRouted, OnMessageSent), `Sessions/SessionManager.cs` (weave OnSessionStart, OnSessionCompact, OnSessionEnd), `Memory/SqliteMemorySystem.cs` (weave OnMemoryWrite, OnMemoryPromote), `Agents/AgentHeartbeatService.cs` (weave OnHeartbeatTick), `Program.cs` (register HookEngine + PluginLoader + PluginPermissionGate, fire OnAgentStart/OnAgentStop), `Config/AgentEntryConfig.cs` (add PluginConfig section), `Skills/SkillInterfaces.cs` (add ISkillProvider), `Scheduling/CronTaskDefinition.cs` (add ICronTaskProvider)
- **New tests**: `HookEngineTests.cs`, `PluginLoaderTests.cs`, `PluginPermissionGateTests.cs`, `PluginIntegrationTests.cs`
- **No breaking changes**: All existing behavior preserved; hooks are opt-in. When no hooks registered, pipeline behaves identically.
- **Dependencies**: No new NuGet packages required. Plugin assembly loading uses `System.Reflection` and `AssemblyLoadContext` (built-in).
