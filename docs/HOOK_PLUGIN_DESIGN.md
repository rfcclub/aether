# Aether Hook & Plugin System — Complete Design

> Version: 1.0
> Author: Aria
> Date: 2026-05-11
> Status: Proposal — For Review

---

## 1. What Is a Plugin?

A plugin is a **self-contained directory** that extends Aether with any combination of: hooks, tools, skills, channels, cron tasks, and middleware. It is the atomic unit of extension.

```
plugins/
└── <plugin-name>/           ← One plugin = one directory
    ├── plugin.json           ← Manifest (REQUIRED)
    ├── <plugin-name>.dll     ← Compiled assembly (optional — hooks + code tools)
    ├── tools/                ← JSON tool definitions (optional)
    │   └── my-tool.json
    ├── skills/               ← SKILL.md files (optional)
    │   └── my-skill.md
    └── cron/                 ← Cron task definitions (optional)
        └── nightly-sync.md
```

**Key principle**: A plugin that only provides skills (markdown) or JSON tools (hot-reload stubs) needs **zero compiled code** — just `plugin.json` + markdown/JSON files.

---

## 2. Plugin Manifest (`plugin.json`)

```jsonc
{
  // ── Identity ──
  "name": "guard-rails",                          // Unique ID, lowercase-kebab
  "version": "2.1.0",                            // Semver
  "displayName": "Guard Rails",                  // Human-readable
  "description": "Safety guardrails for tool execution and LLM output",
  "author": "aria",
  "license": "MIT",
  "homepage": "https://github.com/aria/guard-rails",

  // ── Assembly ──
  "assembly": "GuardRails.dll",                  // Relative to plugin dir. Omit if no code.

  // ── Hooks (code required) ──
  "hooks": [
    {
      "class": "GuardRails.BashBlocker",         // Fully qualified type name
      "points": ["PreToolUse"],                   // HookPoint flags
      "priority": 10                              // Lower = earlier (0 = first)
    },
    {
      "class": "GuardRails.OutputValidator",
      "points": ["PostLlmCall", "OnMessageSent"],
      "priority": 50
    }
  ],

  // ── Tools ──
  "tools": [
    {
      "name": "validate_config",
      "definition": "tools/validate-config.json"  // Path to JSON tool definition
    }
    // Code-registered tools are auto-discovered from IToolImplementation in assembly
  ],

  // ── Skills ──
  "skills": [
    { "name": "security-review", "path": "skills/security-review.md" },
    { "name": "code-audit",      "path": "skills/code-audit.md" }
  ],

  // ── Channels ──
  "channels": [
    {
      "class": "GuardRails.AuditChannel"          // Must implement IChannel
    }
  ],

  // ── Cron ──
  "cron": [
    {
      "name": "nightly-audit",
      "schedule": "0 3 * * *",                   // 3:00 AM daily
      "task": "cron/nightly-audit.md"             // Task definition
    }
  ],

  // ── Dependencies ──
  "dependencies": {
    "aether": ">=3.0.0"                           // Minimum Aether version
  },

  // ── Permissions ──
  "permissions": {
    "network": true,                              // Allow HTTP calls
    "filesystem": ["plugins/guard-rails/**"],     // Allowed file paths
    "tools": ["bash", "web_search"]               // Tools this plugin can call
  }
}
```

---

## 3. All Interfaces a Plugin Can Expose

### 3.1 `IHook` — The primary extension point

```csharp
namespace Aether.Plugins;

/// <summary>
/// A hook intercepts the agent pipeline at specific points.
/// Implementations are discovered from plugin assemblies and DI.
/// </summary>
public interface IHook
{
    /// <summary>Unique identifier for this hook instance.</summary>
    string Name { get; }

    /// <summary>Which hook points this hook subscribes to (flags).</summary>
    HookPoint SubscribesTo { get; }

    /// <summary>
    /// Execution order. Lower numbers run first.
    /// Built-in system hooks: 0-9. Default plugins: 10-50. User plugins: 50+.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Called by HookEngine when a subscribed hook point fires.
    /// Return HookResult.Continue to proceed, HookResult.Stop(reason) to halt.
    /// </summary>
    Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct);
}
```

### 3.2 `IToolImplementation` — Code-backed tools

```csharp
namespace Aether.Tooling;

/// <summary>
/// A tool with real code execution. Auto-registered from plugin assemblies.
/// Hot-reload JSON tools are passive stubs — this interface gives them real behavior.
/// </summary>
public interface IToolImplementation
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }
    Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct);
}
```

**How plugin tools bridge JSON + code:**
- If a plugin has both `tools[].definition` (JSON) AND an `IToolImplementation` with the same `Name`, the JSON provides the schema metadata and the code provides the real execution.
- If only JSON exists → passive stub (logs invocation, returns placeholder).
- If only `IToolImplementation` exists → code provides both metadata and execution.

### 3.3 `IChannel` — Custom channel implementations

```csharp
namespace Aether.Channels;

/// <summary>
/// A communication channel. Plugins can provide custom channels
/// (Discord, Slack, WhatsApp, Email, etc.) by implementing this interface.
/// </summary>
public interface IChannel
{
    string Name { get; }
    bool IsConnected { get; }
    event EventHandler<InboundMessage>? OnMessage;
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task SendMessageAsync(string chatId, string text, CancellationToken ct);
    Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct);
    bool OwnsChatId(string chatId);
    Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct);
    Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct);
}
```

### 3.4 `ISkillProvider` — Skills as data

```csharp
namespace Aether.Skills;

/// <summary>
/// Provides SKILL.md definitions. Plugins implement this to register skills.
/// Skills are loaded at plugin init and merged into the global SkillRegistry.
/// </summary>
public interface ISkillProvider
{
    /// <summary>List of skills this plugin provides.</summary>
    IReadOnlyList<SkillDefinition> GetSkills();

    /// <summary>Optional: validate a skill's body before registration.</summary>
    bool ValidateSkill(SkillDefinition skill, out string? error);
}
```

### 3.5 `ICronTaskProvider` — Scheduled tasks

```csharp
namespace Aether.Scheduling;

/// <summary>
/// Provides cron task definitions. Plugins implement this for scheduled automation.
/// </summary>
public interface ICronTaskProvider
{
    IReadOnlyList<CronTaskDefinition> GetTasks();
}
```

### 3.6 `IPluginLifecycle` — Plugin init/shutdown

```csharp
namespace Aether.Plugins;

/// <summary>
/// Optional lifecycle hooks for the plugin itself.
/// Called by PluginLoader during load/unload.
/// </summary>
public interface IPluginLifecycle
{
    /// <summary>Called when plugin is loaded and all dependencies are ready.</summary>
    Task OnLoadAsync(PluginContext context, CancellationToken ct);

    /// <summary>Called when plugin is being unloaded (shutdown or hot-reload).</summary>
    Task OnUnloadAsync(CancellationToken ct);

    /// <summary>Called when plugin is enabled for a specific agent.</summary>
    Task OnAgentEnabledAsync(string agentName, CancellationToken ct);

    /// <summary>Called when plugin is disabled for a specific agent.</summary>
    Task OnAgentDisabledAsync(string agentName, CancellationToken ct);
}

/// <summary>
/// Context passed to plugins on load, providing access to Aether services.
/// </summary>
public class PluginContext
{
    public string PluginName { get; init; }
    public string PluginDirectory { get; init; }
    public PluginManifest Manifest { get; init; }
    public IServiceProvider Services { get; init; }       // Full DI access
    public ILogger Logger { get; init; }
    public PluginConfigStore Config { get; init; }         // Per-plugin persistent config
}
```

---

## 4. Hook System — Complete Specification

### 4.1 Hook Points (enum flags)

```csharp
[Flags]
public enum HookPoint
{
    None                = 0,

    // ── Message Pipeline ──
    OnMessageReceived   = 1 << 0,   // Message arrives from channel
    OnMessageRouted     = 1 << 1,   // Message matched to agent
    OnMessageSent       = 1 << 2,   // Response about to be sent

    // ── LLM Pipeline ──
    PreLlmCall          = 1 << 3,   // Before LLM API call
    PostLlmCall         = 1 << 4,   // After LLM API response

    // ── Tool Pipeline ──
    PreToolUse          = 1 << 5,   // Before tool execution
    PostToolUse         = 1 << 6,   // After tool execution

    // ── Session ──
    OnSessionStart      = 1 << 7,   // New session created
    OnSessionCompact    = 1 << 8,   // Session being compacted
    OnSessionEnd        = 1 << 9,   // Session ending

    // ── Memory ──
    OnMemoryWrite       = 1 << 10,  // Writing to memory (ephemeral/working/durable)
    OnMemoryPromote     = 1 << 11,  // Candidate being promoted to durable

    // ── Agent Lifecycle ──
    OnAgentStart        = 1 << 12,  // Agent booted and ready
    OnAgentStop         = 1 << 13,  // Agent shutting down
    OnHeartbeatTick     = 1 << 14,  // Heartbeat tick fired

    // ── Convenience ──
    All                 = ~0,
    MessageLifecycle    = OnMessageReceived | OnMessageRouted | OnMessageSent,
    LlmLifecycle        = PreLlmCall | PostLlmCall,
    ToolLifecycle       = PreToolUse | PostToolUse,
    SessionLifecycle    = OnSessionStart | OnSessionCompact | OnSessionEnd,
    AgentLifecycle      = OnAgentStart | OnAgentStop | OnHeartbeatTick,
}
```

### 4.2 Hook Context Types

```csharp
// ── Base context (all hooks receive this) ──
public abstract record HookContext
{
    public string AgentName { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string SessionId { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Shared mutable bag for hooks to pass data to later hooks in the same pipeline.
    /// Example: PreLlmCall hook stores complexity score → PostLlmCall hook reads it.
    /// </summary>
    public Dictionary<string, object?> Bag { get; init; } = new();
}

// ── Message contexts ──
public record OnMessageReceivedContext : HookContext
{
    public string ChatId { get; init; } = "";
    public string SenderId { get; init; } = "";
    public string ChannelName { get; init; } = "";     // "telegram", "websocket"
    public string Text { get; init; } = "";
    public bool Dropped { get; set; }                   // Set to silently drop
    public string? OverrideText { get; set; }           // Transform before routing
}

public record OnMessageRoutedContext : HookContext
{
    public InboundMessage Message { get; init; } = null!;
    public string ResolvedAgentName { get; init; } = "";
    public bool RerouteToAgent { get; set; }            // Change target agent
    public string? RerouteAgentName { get; set; }
}

public record OnMessageSentContext : HookContext
{
    public string ChatId { get; init; } = "";
    public string Text { get; init; } = "";
    public string? OverrideText { get; set; }           // Transform final output
    public bool Suppress { get; set; }                   // Don't send at all
}

// ── LLM contexts ──
public record PreLlmCallContext : HookContext
{
    public string SystemPrompt { get; set; } = "";       // Mutable — hooks can modify
    public IReadOnlyList<LlmMessage> Messages { get; init; } = [];
    public string ModelName { get; init; } = "";
    public string ProviderName { get; init; } = "";
    public bool ShouldEscalate { get; set; }             // Force higher-tier provider
    public int EstimatedTokens { get; init; }
}

public record PostLlmCallContext : HookContext
{
    public LlmResponse Response { get; init; } = null!;
    public int TokensUsed { get; init; }
    public TimeSpan Latency { get; init; }
    public bool ShouldRetry { get; set; }                // Retry LLM call
    public string? RetryReason { get; set; }
    public string? OverrideContent { get; set; }         // Replace response content
}

// ── Tool contexts ──
public record PreToolUseContext : HookContext
{
    public string ToolName { get; init; } = "";
    public JsonElement Arguments { get; init; }
    public string RawArguments { get; init; } = "";
    public ToolRisk Risk { get; init; }
    public bool Denied { get; set; }                     // Block execution
    public string? DenyReason { get; set; }
    public JsonElement? OverrideArguments { get; set; }  // Transform args
}

public record PostToolUseContext : HookContext
{
    public string ToolName { get; init; } = "";
    public JsonElement Arguments { get; init; }
    public object? Result { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public object? OverrideResult { get; set; }          // Transform result seen by LLM
}

// ── Session contexts ──
public record OnSessionStartContext : HookContext
{
    public bool IsNewSession { get; init; }              // true = /new, false = resume
}

public record OnSessionCompactContext : HookContext
{
    public int TokensBefore { get; init; }
    public int TokensAfter { get; set; }
    public string? Summary { get; init; }
}

// ── Memory contexts ──
public record OnMemoryWriteContext : HookContext
{
    public string MemoryLayer { get; init; } = "";       // "ephemeral", "working", "durable"
    public string Content { get; init; } = "";
    public float Confidence { get; init; }
    public bool Denied { get; set; }
    public string? DenyReason { get; set; }
}

// ── Agent lifecycle contexts ──
public record OnAgentStartContext : HookContext
{
    public bool IsFirstBoot { get; init; }
    public string AgentVersion { get; init; } = "";
    public IReadOnlyList<string> BootFiles { get; init; } = [];
}

public record OnHeartbeatTickContext : HookContext
{
    public int TickNumber { get; init; }
    public TimeSpan TimeSinceLastTick { get; init; }
    public string? HeartbeatContent { get; init; }       // HEARTBEAT.md content
}
```

### 4.3 Hook Engine

```csharp
namespace Aether.Plugins;

public class HookEngine
{
    private readonly IReadOnlyList<IHook> _hooks;  // Sorted by Priority asc
    private readonly ILogger<HookEngine> _logger;

    public HookEngine(IEnumerable<IHook> hooks, ILogger<HookEngine> logger)
    {
        _hooks = hooks
            .OrderBy(h => h.Priority)
            .ThenBy(h => h.Name)                // Deterministic tiebreaker
            .ToList();
        _logger = logger;
    }

    /// <summary>
    /// Execute all hooks subscribed to a given point.
    /// Stops on first non-success result (short-circuit).
    /// Returns the first non-success, or Continue.
    /// </summary>
    public async Task<HookResult> RunAsync(
        HookPoint point,
        HookContext context,
        CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            if ((hook.SubscribesTo & point) == 0)
                continue;

            ct.ThrowIfCancellationRequested();

            try
            {
                var sw = Stopwatch.StartNew();
                var result = await hook.ExecuteAsync(context, ct);
                sw.Stop();

                if (sw.ElapsedMilliseconds > 500)
                {
                    _logger.LogWarning(
                        "Slow hook: {HookName} at {Point} took {Ms}ms",
                        hook.Name, point, sw.ElapsedMilliseconds);
                }

                if (!result.Success)
                {
                    _logger.LogInformation(
                        "Hook {HookName} stopped pipeline at {Point}: {Reason}",
                        hook.Name, point, result.StopReason);
                    return result;
                }
            }
            catch (Exception ex)
            {
                // A hook throwing does NOT stop the pipeline —
                // it's logged and execution continues with the next hook.
                _logger.LogError(ex,
                    "Hook {HookName} threw at {Point} — continuing pipeline",
                    hook.Name, point);
            }
        }

        return HookResult.Continue;
    }

    /// <summary>
    /// Run hooks that don't short-circuit (fire-and-forget style).
    /// All hooks run regardless of individual results. Used for Post* hooks.
    /// </summary>
    public async Task RunAllAsync(
        HookPoint point,
        HookContext context,
        CancellationToken ct)
    {
        foreach (var hook in _hooks)
        {
            if ((hook.SubscribesTo & point) == 0)
                continue;

            ct.ThrowIfCancellationRequested();

            try
            {
                await hook.ExecuteAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook {HookName} threw at {Point}", hook.Name, point);
            }
        }
    }

    public IReadOnlyList<HookInfo> GetRegisteredHooks()
        => _hooks.Select(h => new HookInfo(h.Name, h.SubscribesTo, h.Priority)).ToList();
}

public record HookInfo(string Name, HookPoint SubscribesTo, int Priority);

public readonly struct HookResult
{
    public bool Success { get; init; }
    public string? StopReason { get; init; }

    public static HookResult Continue => new() { Success = true };
    public static HookResult Stop(string reason) => new() { Success = false, StopReason = reason };
}
```

---

## 5. Plugin Loading & Lifecycle

### 5.1 Discovery Flow

```
Aether Host Startup
    │
    ▼
PluginLoader.LoadAllAsync()
    │
    ├── Scan plugins/ for directories containing plugin.json
    │
    ├── Parse & validate all manifests
    │
    ├── Dependency resolution (topological sort)
    │   └── Error if: missing dependency, circular dependency, incompatible version
    │
    ├── For each plugin (in dependency order):
    │   ├── If assembly specified:
    │   │   ├── Load assembly via AssemblyLoadContext (isolated, collectible)
    │   │   ├── Discover IHook implementations  → register in HookEngine
    │   │   ├── Discover IToolImplementation    → register in ToolRegistry
    │   │   ├── Discover IChannel               → register in ChannelManager
    │   │   ├── Discover ISkillProvider         → register in SkillRegistry
    │   │   ├── Discover ICronTaskProvider      → register in CronScheduler
    │   │   └── Discover IPluginLifecycle       → call OnLoadAsync()
    │   │
    │   ├── Load JSON tool definitions          → ToolRegistry.Register()
    │   ├── Load SKILL.md files                 → SkillRegistry.Register()
    │   └── Load cron task definitions          → CronScheduler.Register()
    │
    └── Return PluginLoadResult with all discovered components
```

### 5.2 Assembly Loading (Isolation)

```csharp
public class IsolatedPluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public IsolatedPluginLoadContext(string pluginPath, string pluginName)
        : base(pluginName, isCollectible: true)       // Collectible = unloadable
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // Plugin's own dependencies first
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path is not null)
            return LoadFromAssemblyPath(path);

        // Fall back to shared Aether assemblies (intentional sharing)
        if (name.FullName.StartsWith("Aether.") || name.FullName.StartsWith("Aether"))
            return null;  // Let default context handle it

        return null;
    }
}
```

**Assembly isolation rules:**
- Each plugin gets its own `AssemblyLoadContext`
- Plugins can reference Aether assemblies (shared, not duplicated)
- Plugin dependencies resolved from plugin directory first
- `isCollectible: true` means plugins can be unloaded (hot-reload)

### 5.3 Plugin Lifecycle States

```
 ┌──────────┐
 │INSTALLED │  ← plugin.json found, dependency check passed
 └────┬─────┘
      │  enable
      ▼
 ┌──────────┐
 │ ENABLED  │  ← assembly loaded, hooks/tools registered
 └────┬─────┘
      │  on every agent boot
      ▼
 ┌──────────┐
 │  ACTIVE   │  ← hooks are executing, tools callable
 └────┬─────┘
      │  disable / uninstall
      ▼
 ┌──────────┐
 │ DISABLED │  ← assembly unloaded, hooks removed
 └────┬─────┘
      │  uninstall
      ▼
 ┌──────────┐
 │ REMOVED  │  ← directory deleted
 └──────────┘
```

### 5.4 Install/Uninstall CLI

```bash
# Install a plugin from a local directory
aether plugin install ./my-plugin/

# Install from a git repository
aether plugin install https://github.com/aria/aether-guard-rails

# List installed plugins with status
aether plugin list
# Output:
#   guard-rails     v2.1.0  ACTIVE    hooks: 2, tools: 1, skills: 2
#   analytics       v1.0.0  DISABLED  hooks: 1
#   github-bot      v0.9.0  ACTIVE    hooks: 3, tools: 2, skills: 1

# Enable/disable per agent
aether plugin enable guard-rails --agent maria
aether plugin disable analytics --agent maria

# Show plugin details
aether plugin show guard-rails
# Output: manifest, hooks registered, tools, permissions, dependency graph

# Uninstall
aether plugin uninstall guard-rails [--force]
```

---

## 6. Plugin Permissions & Sandbox

Plugins declared with `"permissions": {}` in manifest. If no permissions declared → minimal defaults (no network, no filesystem outside plugin dir).

```csharp
public class PluginPermissionGate
{
    /// <summary>
    /// Validates that a plugin's declared permissions match its actual behavior.
    /// Called at load time and at runtime for tool calls.
    /// </summary>
    public bool ValidateRequest(
        PluginManifest manifest,
        string requestedPermission,
        object? context = null)
    {
        var perms = manifest.Permissions ?? PluginPermissions.Default;

        return requestedPermission switch
        {
            "network"       => perms.Network,
            "filesystem"    => IsPathAllowed(context as string, perms.Filesystem),
            "tool_call"     => IsToolAllowed(context as string, perms.Tools),
            "channel_send"  => perms.Channels.Contains(context as string ?? ""),
            _               => false
        };
    }
}

public class PluginPermissions
{
    public bool Network { get; init; } = false;
    public List<string> Filesystem { get; init; } = new();    // Glob patterns
    public List<string> Tools { get; init; } = new();          // Tool names
    public List<string> Channels { get; init; } = new();       // Channel names

    public static PluginPermissions Default => new()
    {
        Network = false,
        Filesystem = new() { "plugins/<self>/**" },            // Only own directory
        Tools = new(),
        Channels = new()
    };
}
```

---

## 7. Per-Agent Plugin Configuration

```jsonc
// ~/.aether/workspaces/maria/.aether.json
{
  "plugins": {
    // ── Which plugins are active for this agent ──
    "enabled": [
      "guard-rails",
      "persona-injector",
      "auto-escalate",
      "memory-archiver"
    ],
    "disabled": [
      "analytics"               // Analytics not needed for Maria
    ],

    // ── Per-plugin configuration overrides ──
    "config": {
      "guard-rails": {
        "blockedCommands": ["rm -rf", "sudo", "chmod 777"],
        "requireConfirmation": ["git push --force", "docker rm"],
        "maxBashTimeoutSeconds": 120
      },
      "persona-injector": {
        "tone": "warm",
        "formality": "casual",
        "extraContext": "You are speaking with Thoor, your creator."
      },
      "auto-escalate": {
        "complexityThreshold": 0.7,
        "escalationProvider": "anthropic",
        "safetyKeywords": ["production", "database", "migration", "security"]
      },
      "memory-archiver": {
        "archiveAfterDays": 7,
        "backupPath": "/backups/maria"
      }
    },

    // ── Hook-specific priority overrides ──
    "hookOverrides": {
      "guard-rails/BashBlocker": { "priority": 1 },       // Run first
      "memory-archiver/ArchiveHook": { "priority": 100 }  // Run last
    }
  }
}
```

---

## 8. Integration Points in Aether Codebase

### 8.1 Where hooks fire (modifications needed)

```
File: ChannelMessageProcessor.cs
  → OnMessageReceived  (before access check)
  → OnMessageRouted    (after routing, before agent dispatch)
  → OnMessageSent      (before channel.SendMessageAsync)

File: Agent/AetherSoul.cs
  → PreLlmCall         (before _llm.CompleteStreamingEventsAsync)
  → PostLlmCall        (after LLM response, before tool loop exit)
  → PreToolUse         (before ExecuteToolCallAsync)
  → PostToolUse        (after ExecuteToolCallAsync)

File: Sessions/SessionManager.cs
  → OnSessionStart     (after CreateSessionAsync)
  → OnSessionCompact   (after CompactSessionAsync)
  → OnSessionEnd       (on session terminate)

File: Memory/SqliteMemorySystem.cs
  → OnMemoryWrite      (before/after write to any layer)
  → OnMemoryPromote    (on promotion candidate → durable)

File: Agents/AgentHeartbeatService.cs
  → OnHeartbeatTick    (on each timer tick)

File: Program.cs (RunServeAsync)
  → OnAgentStart       (after DI built, before host.RunAsync)
  → OnAgentStop        (on host shutdown)
```

### 8.2 AetherSoul integration (key example)

```csharp
// Modified ProcessStreamingAsync in AetherSoul.cs
public async IAsyncEnumerable<string> ProcessStreamingAsync(
    string groupFolder, string prompt, ...)
{
    _ctx.AddUser(prompt);

    // ──────────────── PreLlmCall HOOK ────────────────
    var preCtx = new PreLlmCallContext
    {
        AgentName = _ctx.AgentName,
        WorkspacePath = _ctx.WorkspacePath,
        SessionId = _ctx.SessionId,
        SystemPrompt = _ctx.SystemPrompt,
        Messages = _ctx.Messages.ToList(),
        ModelName = _llm.EffectiveModel,
        ProviderName = _llm.EffectiveProvider,
    };
    var preResult = await _hooks.RunAsync(HookPoint.PreLlmCall, preCtx, ct);
    if (!preResult.Success)
    {
        yield return $"[Hook blocked: {preResult.StopReason}]";
        yield break;
    }

    // If a hook escalated, re-resolve provider
    if (preCtx.ShouldEscalate)
    {
        // _llm.ForceEscalate() — switch to next tier provider
    }

    // Use potentially modified system prompt
    var messages = preCtx.SystemPrompt != _ctx.SystemPrompt
        ? RebuildMessages(preCtx.SystemPrompt, _ctx.Messages)
        : _ctx.Messages;

    // ... existing LLM call + tool loop ...

    // ──────────────── PreToolUse HOOK ────────────────
    foreach (var toolCall in toolCalls)
    {
        var preToolCtx = new PreToolUseContext
        {
            AgentName = _ctx.AgentName,
            WorkspacePath = _ctx.WorkspacePath,
            SessionId = _ctx.SessionId,
            ToolName = toolCall.Name,
            Arguments = toolCall.Arguments,
            RawArguments = JsonSerializer.Serialize(toolCall.Arguments),
            Risk = ToolRiskFor(toolCall.Name),
        };
        var preToolResult = await _hooks.RunAsync(HookPoint.PreToolUse, preToolCtx, ct);

        if (preToolCtx.Denied)
        {
            _ctx.AddToolResult(toolCall.Id, toolCall.Name,
                $"Tool '{toolCall.Name}' blocked: {preToolCtx.DenyReason ?? "policy"}");
            continue;
        }

        var args = preToolCtx.OverrideArguments ?? toolCall.Arguments;
        var result = await ExecuteToolCallAsync(
            new LlmToolCall(toolCall.Id, toolCall.Name, args), ct);

        // ──────────────── PostToolUse HOOK ────────────────
        var postToolCtx = new PostToolUseContext
        {
            AgentName = _ctx.AgentName,
            WorkspacePath = _ctx.WorkspacePath,
            ToolName = toolCall.Name,
            Arguments = args,
            Result = result,
            Success = !result.StartsWith("Tool failed:"),
        };
        await _hooks.RunAllAsync(HookPoint.PostToolUse, postToolCtx, ct);

        var finalResult = postToolCtx.OverrideResult?.ToString() ?? result;
        _ctx.AddToolResult(toolCall.Id, toolCall.Name, finalResult);
    }

    // ──────────────── PostLlmCall HOOK ────────────────
    var postCtx = new PostLlmCallContext
    {
        AgentName = _ctx.AgentName,
        WorkspacePath = _ctx.WorkspacePath,
        SessionId = _ctx.SessionId,
        Response = finalResponse,
        TokensUsed = estimatedTokens,
        Latency = sw.Elapsed,
    };
    await _hooks.RunAllAsync(HookPoint.PostLlmCall, postCtx, ct);

    var output = postCtx.OverrideContent ?? finalResponse.Content;
    _ctx.AddAssistant(output);
    yield return output;
}
```

---

## 9. Implementation Roadmap

| Phase | Scope | Deliverables | Est. Files |
|-------|-------|-------------|------------|
| **P0: Hook Engine** | `IHook`, `HookEngine`, `HookPoint`, all `HookContext` types, `HookResult` | Core hook infrastructure, unit tests | 5 files |
| **P1: Weave Hooks** | Integrate hooks into `AetherSoul`, `ChannelMessageProcessor`, `SessionManager`, `AgentHeartbeatService`, `Program.cs` | Hooks fire at all 14 hook points | Mod 5 files |
| **P2: Plugin Manifest** | `PluginManifest`, `PluginLoader`, `PluginLoadResult`, manifest validation | Plugins discoverable from `plugins/` | 3 files |
| **P3: Assembly Loading** | `IsolatedPluginLoadContext`, type discovery (`IHook`, `IToolImplementation`, `IChannel`, `ISkillProvider`, `ICronTaskProvider`, `IPluginLifecycle`) | Code-backed plugins work | 2 files |
| **P4: Plugin CLI** | `aether plugin install/list/show/enable/disable/uninstall` | User-facing plugin management | 2 files |
| **P5: Permissions** | `PluginPermissions`, `PluginPermissionGate`, runtime enforcement | Sandbox for plugin code | 2 files |
| **P6: Port Existing** | Refactor existing hardcoded behavior into built-in hooks (ChannelAccess → `OnMessageReceived`, ParameterValidator → `PreToolUse`, auto-escalate → `PreLlmCall`) | Clean separation of concerns | 3 files |
| **P7: Hot Reload** | Plugin file watching, unload/reload cycle, state preservation | No-restart plugin updates | 2 files |

**Total: ~19 new files, ~5 modified files.**

---

## 10. Migration Path — From Current Code to Plugin Architecture

### Before (hardcoded in ChannelMessageProcessor):
```csharp
// Access control inline
var access = await _channelAccess.CheckAccessAsync(message.SenderId, ct);
if (access == AccessResult.Denied) return;
```

### After (hook-based):
```csharp
// OnMessageReceived hook fires; ChannelAccessGate implements IHook
var msgCtx = new OnMessageReceivedContext { ... };
var result = await _hooks.RunAsync(HookPoint.OnMessageReceived, msgCtx, ct);
if (!result.Success || msgCtx.Dropped) return;
```

The existing `ChannelAccess`, `ParameterValidator`, `SlashCommandHandler` all become hook implementations — same logic, cleaner architecture, user-extensible.

---

## 11. Summary

| Question | Answer |
|----------|--------|
| What does a plugin provide? | Hooks, tools, skills, channels, cron tasks — any combination |
| What interfaces does it expose? | `IHook`, `IToolImplementation`, `IChannel`, `ISkillProvider`, `ICronTaskProvider`, `IPluginLifecycle` |
| Code required? | No. JSON tools + SKILL.md skills work without any assembly |
| How are hooks ordered? | `Priority` int (0-100), lower = earlier. Configurable per-agent |
| Can hooks block? | Yes — `HookResult.Stop(reason)` short-circuits the pipeline |
| Can hooks transform? | Yes — `OverrideText`, `OverrideArguments`, `OverrideResult`, `OverrideContent` |
| Are plugins isolated? | Per-plugin AssemblyLoadContext, `isCollectible: true` |
| Are plugins sandboxed? | Permission manifest + PluginPermissionGate enforcement |
| Per-agent config? | Yes — `.aether.json` can enable/disable/configure per agent |
| Hot-reload? | Phase 7 — file watcher + assembly reload |
