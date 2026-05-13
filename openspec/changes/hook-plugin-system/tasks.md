## 1. Hook Core Infrastructure

- [x] 1.1 Create `Plugins/` directory and `HookPoint` flags enum with all 14 hook points and convenience combinations
- [x] 1.2 Create `HookResult` readonly struct with `Success`, `StopReason`, `Continue` factory, and `Stop()` factory
- [x] 1.3 Create base `HookContext` record with `AgentName`, `WorkspacePath`, `SessionId`, `Timestamp`, `Bag`
- [x] 1.4 Create typed hook context records for all 14 hook points (PreLlmCallContext, PreToolUseContext, PostToolUseContext, PostLlmCallContext, OnMessageReceivedContext, OnMessageRoutedContext, OnMessageSentContext, OnSessionStartContext, OnSessionCompactContext, OnMemoryWriteContext, OnAgentStartContext, OnHeartbeatTickContext)
- [x] 1.5 Create `IHook` interface in `Aether.Plugins` namespace with `Name`, `SubscribesTo`, `Priority`, `ExecuteAsync`
- [x] 1.6 Create `HookEngine` with `RunAsync` (short-circuit) and `RunAllAsync` (fire-and-forget) methods, priority sorting, exception isolation, and timeout monitoring
- [x] 1.7 Create `HookEngineTests` covering: priority ordering, short-circuit on Stop, exception isolation, empty hooks no-op, timeout warning

## 2. Plugin Manifest & Loading

- [x] 2.1 Create `PluginManifest` record with all fields matching `plugin.json` schema (name, version, displayName, description, author, license, homepage, assembly, hooks, tools, skills, channels, cron, dependencies, permissions)
- [x] 2.2 Create `PluginPermissions` record with `Network`, `Filesystem`, `Tools`, `Channels`, `Services` properties and secure defaults
- [x] 2.3 Create `PluginLoader` that scans `plugins/` for subdirectories with `plugin.json`, parses manifests, and validates required fields
- [x] 2.4 Implement topological sort for plugin dependency resolution with circular dependency detection
- [x] 2.5 Create `IsolatedPluginLoadContext` inheriting `AssemblyLoadContext` with `isCollectible: true` and directory-first dependency resolution
- [x] 2.6 Implement assembly type discovery: scan loaded assembly for `IHook`, `IToolImplementation`, `IChannel`, `ISkillProvider`, `ICronTaskProvider`, `IPluginLifecycle`
- [x] 2.7 Create `PluginLoadResult` record holding discovered hooks, tools, skills, channels, cron tasks
- [x] 2.8 Create `PluginLoaderTests` covering: manifest parsing, missing name field, directory without manifest, dependency ordering, circular dependency detection

## 3. Plugin Permission Gate

- [x] 3.1 Create `PluginPermissionGate` implementing `IServiceProvider` that wraps the real provider and filters by manifest-declared `services` array
- [x] 3.2 Implement runtime permission checks: network access gate, filesystem path validation against declared globs, tool call gate
- [x] 3.3 Create `PluginContext` record with `PluginName`, `PluginDirectory`, `Manifest`, `Services` (filtered), `Logger`, `Config`
- [x] 3.4 Create `PluginPermissionGateTests` covering: declared service resolved, undeclared service returns null, network denied, filesystem path denied

## 4. Plugin Extension Interfaces

- [x] 4.1 Create `IPluginLifecycle` interface with `OnLoadAsync`, `OnUnloadAsync`, `OnAgentEnabledAsync`, `OnAgentDisabledAsync`
- [x] 4.2 Create `ISkillProvider` interface with `GetSkills()` and `ValidateSkill()`
- [x] 4.3 Create `ICronTaskProvider` interface with `GetTasks()`
- [x] 4.4 Implement plugin asset registration: JSON tools merged into `ToolRegistry`, SKILL.md files into `SkillRegistry`, cron tasks into `CronScheduler`

## 5. Weave Hooks into AetherSoul

- [x] 5.1 Add `HookEngine` dependency to `AetherSoul` constructors (nullable for backward compat — prompt harness)
- [x] 5.2 Fire `PreLlmCall` hook in `ProcessStreamingAsync` before LLM request with mutable `SystemPrompt` and `ShouldEscalate`
- [x] 5.3 Fire `PreToolUse` hook before each `ExecuteToolCallAsync` with deny and override-arguments support
- [x] 5.4 Fire `PostToolUse` hook after each tool execution with override-result support
- [x] 5.5 Fire `PostLlmCall` hook after tool loop completes with `ShouldRetry` and `OverrideContent` support
- [x] 5.6 Verify zero-hook path: when `_hooks` is null, all hook calls are no-ops and behavior unchanged
- [x] 5.7 Update `AetherSoulTests` to cover: hook modifies system prompt, hook denies tool, hook transforms result, hook escalates provider

## 6. Weave Hooks into ChannelMessageProcessor

- [x] 6.1 Add `HookEngine` dependency to `ChannelMessageProcessor`
- [x] 6.2 Fire `OnMessageReceived` hook before access control with drop and override-text support
- [x] 6.3 Fire `OnMessageRouted` hook after agent resolution with reroute support
- [x] 6.4 Fire `OnMessageSent` hook before channel delivery with suppress and override-text support

## 7. Weave Hooks into Remaining Services

- [x] 7.1 Fire `OnSessionStart` hook in `SessionManager.CreateSessionAsync`
- [x] 7.2 Fire `OnSessionCompact` hook in `SessionManager.CompactSessionAsync`
- [x] 7.3 Fire `OnMemoryWrite` hook in `FileMemory.AddToContext` and `SqliteMemorySystem` write paths
- [x] 7.4 Fire `OnMemoryPromote` hook in `SqliteMemorySystem.TryPromoteAsync`
- [x] 7.5 Fire `OnHeartbeatTick` hook in `AgentHeartbeatService.TickAsync`
- [x] 7.6 Fire `OnAgentStart` hook in `Program.RunServeAsync` after boot before host.RunAsync
- [x] 7.7 Fire `OnAgentStop` hook in `Program.RunServeAsync` on shutdown signal

## 8. DI Registration in Program.cs

- [x] 8.1 Register `PluginLoader` as singleton in DI, invoked during host startup
- [x] 8.2 Register `PluginPermissionGate` as singleton
- [x] 8.3 Register `HookEngine` as singleton, resolving hooks from both DI (built-in) and `PluginLoader` (plugin-discovered)
- [x] 8.4 Ensure `HookEngine` is injected into `AetherSoul`, `ChannelMessageProcessor`, `SessionManager`, `AgentHeartbeatService`

## 9. Per-Agent Plugin Configuration

- [x] 9.1 Add `AgentPluginConfig` class with `Enabled`, `Disabled`, `Config` (Dictionary), `HookOverrides` (Dictionary) properties
- [x] 9.2 Add `plugins` section parsing to `ConfigLoader` for `.aether.json`
- [x] 9.3 Implement agent-specific plugin filtering: when processing a message, only hooks from enabled plugins fire
- [x] 9.4 Implement hook priority override from `hookOverrides` config

## 10. Plugin CLI

- [x] 10.1 Add `PluginCli` class for `aether plugin` subcommands
- [x] 10.2 Implement `aether plugin install <path>` — copy directory, validate manifest
- [x] 10.3 Implement `aether plugin list` — table of installed plugins with status
- [x] 10.4 Implement `aether plugin show <name>` — full manifest + registered components
- [x] 10.5 Implement `aether plugin enable <name> --agent <agent>` — update agent .aether.json
- [x] 10.6 Implement `aether plugin disable <name> --agent <agent>` — update agent .aether.json
- [x] 10.7 Implement `aether plugin uninstall <name> [--force]` — remove plugin directory
- [x] 10.8 Wire `PluginCli` into `AetherCli.BuildRootCommand()` as `plugin` subcommand

## 11. Integration Tests

- [ ] 11.1 Create `HookIntegrationTests` verifying end-to-end hook pipeline: message received → PreLlmCall → LLM → PreToolUse → tool execute → PostToolUse → PostLlmCall → OnMessageSent
- [ ] 11.2 Create `PluginIntegrationTests` verifying: plugin with hooks+tools+skills loads, hooks fire in correct order, tools registered and callable
- [ ] 11.3 Verify zero-plugin startup: host starts normally with empty `plugins/` directory
- [x] 11.4 Verify all existing tests (404/405) continue to pass — 1 flaky TavilyWebSearch external API test. Hook system is purely additive.
