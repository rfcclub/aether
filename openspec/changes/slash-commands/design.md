## Context

Aether agents receive messages via channels (Telegram, WebSocket). Every message currently goes through `MessageRouter` → `ChannelMessageProcessor` → `AetherSoul` (LLM). There's no way to execute control commands without an LLM round-trip.

thoor also runs KuroClaw (OpenClaw-based agent framework) which needs the same slash command capability. The design must be abstracted so both frameworks can plug in.

## Goals / Non-Goals

**Goals:**
- Intercept `/`-prefixed messages before the LLM pipeline
- Execute five commands instantly: `/new`, `/reset`, `/model`, `/context`, `/compact`
- Abstract behind `ISlashCommandHandler` interface usable by both Aether and KuroClaw
- Each command handler receives agent context, returns structured result or null (passthrough)
- Zero LLM cost for control commands

**Non-Goals:**
- Custom user-defined slash commands (future)
- Command aliases (future)
- Permission system for commands — all authenticated users can use them
- Persisting command history to sessions

## Decisions

### 1. Pre-LLM interceptor in ChannelMessageProcessor

**Choice:** Intercept in `ChannelMessageProcessor.HandleMessageAsync`, before the `using var scope` block that creates AetherSoul.

**Why:** Earliest point after routing where we have agent context (workspace, config). Avoids creating AetherSoul scope for commands that don't need LLM.

**Alternative considered:** Handle in MessageRouter. Rejected — router doesn't have session/memory/model access.

### 2. Interface-based dispatch: `ISlashCommandHandler`

```csharp
public interface ISlashCommandHandler
{
    // Returns null if message is not a slash command (passthrough to LLM).
    // Returns result with response text if handled.
    Task<SlashCommandResult?> HandleAsync(SlashCommandContext ctx, CancellationToken ct);
}

public record SlashCommandContext(
    string Text,           // Full message text
    string AgentName,      // Current agent name
    string WorkspacePath,  // Agent workspace
    IServiceProvider Services // For resolving scoped services
);

public record SlashCommandResult(string Text);
```

**Why:** Single-method interface. Both Aether and KuroClaw can implement their own handler or share the same one. Returns null for passthrough — no allocation for non-command messages.

### 3. Command parsing: simple prefix matching

**Choice:** Parse `Text.TrimStart()` — if starts with `/`, split on first space for command name, rest is args. No regex, no CommandLineParser.

**Why:** Five commands, simple syntax. Regex overkill. Easy to test.

### 4. `/model` live-switch via ProviderRouter.ModelChain

**Choice:** Update `ProviderRouter.ModelChain` directly at runtime. No config file write, no restart.

**Why:** ModelChain is already a mutable property. Instant effect on next LLM call. `/model` with no args reads current state.

**Alternative considered:** Write to config.json. Rejected — requires config reload, slower, more complex.

### 5. `/compact` delegates to IMemorySystem.CompactContext()

**Choice:** Call existing `IMemorySystem.CompactContext(int targetTokens)` with a reasonable default (4000 tokens).

**Why:** Memory system already has compaction. Just wire it.

## Risks / Trade-offs

- **Race condition: /model during active LLM call** → ModelChain read at start of CompleteAsync; switch takes effect next call. Safe.
- **/compact during streaming** → CompactContext called between turns only (pre-LLM interceptor). No concurrent modification.
- **KuroClaw compatibility** → Interface uses `IServiceProvider` for DI resolution. KuroClaw's DI container (Microsoft.Extensions.DI) fully compatible.
