## Context

Aether's current architecture has implicit extension points: `IToolImplementation` for tools, `SKILL.md` for skills, `IChannel` for channels. But the central agent pipeline — message routing, LLM calls, tool execution, session management — is hardcoded in `ChannelMessageProcessor.HandleMessageAsync()` and `AetherSoul.ProcessStreamingAsync()`. There is no way to intercept, transform, or block at these points without modifying source code.

The codebase already has the shape of a pipeline: access check → route → slash command → configure → LLM → tools → response. This is a middleware pattern waiting to be made explicit.

Full design documents: `docs/HOOK_PLUGIN_DESIGN.md` (complete specification) and `docs/PLUGIN_POWER_CEILING.md` (power ceiling analysis).

## Goals / Non-Goals

**Goals:**
- Formalize 14 hook points as `HookPoint` flags enum spanning message, LLM, tool, session, memory, and agent lifecycle
- `IHook` interface with priority-ordered execution and short-circuit semantics
- `HookEngine` that runs hooks in priority order, stopping pipeline on first non-success result for Pre hooks, fire-and-forget for Post hooks
- Plugin system: self-contained directories with `plugin.json` manifest, optional assembly, bundled tools/skills/channels/cron
- Capability-based service access: plugins declare required DI services in manifest; `PluginPermissionGate` wraps `IServiceProvider`
- Isolated assembly loading per plugin via `IsolatedPluginLoadContext` (collectible, independent resolution)
- Per-agent plugin configuration in `.aether.json`
- CLI for plugin lifecycle management
- Zero breaking changes: when no hooks are registered, pipeline behavior is identical to current

**Non-Goals:**
- Hot-reload of plugin assemblies (Phase 7, future)
- Plugin marketplace or remote installation from URL (future)
- Plugin-to-plugin communication (intentional isolation)
- Replacing ToolRegistry or SkillRegistry — plugins add to them, not replace
- Plugin subprocess isolation (plugins run in-process; sandbox is manifest-based, not OS-level)
- Porting existing hardcoded behavior into hooks in this change (separate refactor change)

## Decisions

### 1. Hook Architecture: Priority-Ordered Pipeline vs Event-Based Pub/Sub

**Chosen: Priority-ordered pipeline with short-circuit semantics.**

Rationale:
- Deterministic execution order (`int Priority`, lower = earlier, tiebreak by name)
- Pre hooks can block the pipeline (`HookResult.Stop(reason)`) — critical for guard-rails, access control, validation
- Post hooks are fire-and-forget (`HookEngine.RunAllAsync`) — observers don't block
- Simpler to reason about than pub/sub with multiple subscribers and no ordering guarantee

Alternatives considered:
- Event-based pub/sub: More flexible but non-deterministic ordering, no short-circuit, harder to debug
- ASP.NET Core middleware: Requires request/response pair; Aether's pipeline is more complex (LLM loop with tool iterations)

### 2. Hook Context: Typed Records vs Generic Dictionary

**Chosen: Typed C# records per hook point.**

Each hook point gets its own context type (e.g., `PreLlmCallContext`, `PreToolUseContext`) with named, typed properties. Shared fields (AgentName, WorkspacePath, SessionId, Timestamp) in base `HookContext` record. Mutable fields for transform hooks (e.g., `PreLlmCallContext.SystemPrompt` is a mutable string, `PreToolUseContext.Denied` is a settable bool). A `Dictionary<string, object?> Bag` allows hooks to pass arbitrary state to later hooks in the same pipeline.

Rationale:
- Compile-time safety: hooks know exactly what fields are available for their hook point
- Self-documenting: the context type IS the contract
- Mutable transform fields avoid allocation overhead of immutable records with `with` expressions
- `Bag` addresses the "unknown future use case" without sacrificing type safety for known ones

Alternatives considered:
- Single generic `HookContext` with everything nullable: Type-unsafe, confusing API
- Immutable records with `with` copies: Allocation pressure, awkward for multi-field transforms

### 3. Exception Handling: Catch-and-Continue vs Fail-Fast

**Chosen: Exception in one hook is caught, logged, and pipeline continues.**

Rationale:
- A third-party plugin should never crash the agent
- A buggy hook at priority 10 should not prevent a security hook at priority 50 from running
- Hook execution is wrapped in try/catch; exceptions are logged at Error level
- The pipeline proceeds to the next hook

Trade-off: A hook that silently throws might be missed. Mitigation: hook timeout warnings (>500ms) and error logging ensure visibility.

### 4. Plugin Assembly Loading: Per-Plugin ALC vs Shared Context

**Chosen: `IsolatedPluginLoadContext` per plugin, `isCollectible: true`.**

Rationale:
- Prevents dependency conflicts between plugins (Plugin A depends on Newtonsoft.Json 12, Plugin B on 13)
- Enables future hot-reload (collectible ALCs can be unloaded)
- Intentional sharing of Aether assemblies (default context handles them, plugins reference shared)
- Plugin's own dependencies resolved from its directory first

Alternatives considered:
- Single shared `AssemblyLoadContext`: Simpler but dependency conflicts inevitable
- Separate processes: Strongest isolation but IPC overhead, complexity, harder tool/hook integration

### 5. Service Access: Capability-Based vs Full DI vs Interface Filtering

**Chosen: Capability-based. Plugins declare `"services": ["AetherDb", "SessionManager"]` in manifest.**

`PluginPermissionGate` wraps `IServiceProvider` and only resolves services listed in the manifest's `permissions.services` array. Unlisted services resolve to `null`.

Rationale:
- Plugin authors must explicitly declare what they need — conscious choice, not accidental access
- Auditable: `aether plugin show <name>` displays declared capabilities
- Prevents "slippery slope" where plugins quietly depend on internal services
- Still powerful enough for real plugins (DB access, provider routing, session management all available if declared)

### 6. Plugin Discovery: Directory Scanning vs Registry Manifest

**Chosen: Scan `plugins/` directory for subdirectories containing `plugin.json`.**

Rationale:
- Installing a plugin = copying a directory (or `git clone`)
- Uninstalling = deleting a directory
- No central registry to maintain or corrupt
- Self-contained: everything the plugin needs is in its directory
- CLI commands (`aether plugin install <path>`) manage the directory

### 7. Hook Weaving: Direct Modification vs Decorator/Proxy Pattern

**Chosen: Direct modification of `AetherSoul`, `ChannelMessageProcessor`, and other classes.**

Add `HookEngine` as a dependency injected into these classes. Call `_hooks.RunAsync(point, context, ct)` at specific points in the method bodies.

Rationale:
- Minimal abstraction overhead — no wrapper classes, no interface explosion
- Clear insertion points: each hook call is a single await statement
- `HookEngine` can be null (no hooks registered) → hook calls are no-ops, fast path
- Easier to follow in debugger than decorator chains

## Risks / Trade-offs

| Risk | Mitigation |
|------|-----------|
| Hook execution adds latency to every message | Short-circuit when `_hooks` is empty; timeout monitor (>500ms warning, >5s kill); most hooks complete in <1ms |
| Plugin with buggy infinite loop | Hook timeout kills after 5s; plugin marked as unhealthy; admin can disable per-agent |
| Plugin declares `"services": ["AetherDb"]` and reads other agents' data | `PluginPermissionGate` audit log records all service access; future: DB-level agent_id filtering |
| Plugin with `"network": true` exfiltrates data | Manifest-enforced; network permission is opt-in, default OFF; audit log records all HTTP calls |
| Plugin assembly loading fails | Failure is logged; remaining plugins continue loading; agent starts with partial plugin set |
| Breaking change if HookEngine API evolves | `IHook` interface designed to be stable; new hook points are additive (new flags); HookContext types can gain optional properties without breaking |
| Plugin A depends on Plugin B but load order is wrong | Topological sort by `dependencies` field in manifest; circular dependency → load error, both disabled |
