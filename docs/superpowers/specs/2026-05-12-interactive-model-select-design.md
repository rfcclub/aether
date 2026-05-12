# Interactive Model Selection UI — Design Spec

**Date**: 2026-05-12  
**Status**: Draft — awaiting review  
**Scope**: Aether — all channels (Telegram first, WebSocket + TUI follow)

## Problem

Current `/model` and `/models` slash commands return **plain text only** across all channels. On Telegram specifically:

1. Model listing is text-only — no interactive buttons, no visual grouping
2. Providers are shown as text headers but not visually distinct
3. Changing model requires typing the exact model ID string — no tap-to-select
4. No visual indicator of which model is currently active beyond a `<- current` text marker

## Goal

Replace text-only model selection with **channel-native interactive UI**:

- **Telegram**: Inline keyboards grouped by provider, one-tap model switching, visual current-model indicator
- **WebSocket**: JSON interactive messages with action-based callbacks
- **TUI**: Terminal.Gui controls (future)

## Design

### 1. UiDocument — Channel-Agnostic UI Data Model

All slash commands produce a `UiDocument` — a pure data record describing what to display and what actions are available. Channels render it to their native format.

```csharp
public sealed record UiDocument
{
    public string Text { get; init; }
    public List<UiSection> Sections { get; init; } = new();
    public UiLayout Layout { get; init; } = UiLayout.List;
    public string CallbackNamespace { get; init; } = "";
}

public sealed record UiSection
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public List<UiItem> Items { get; init; } = new();
}

public sealed record UiItem
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Emoji { get; init; }
    public bool Selected { get; init; }
    public string? StatusBadge { get; init; }
}

public enum UiLayout { List, Grid, Paged }
```

### 2. IUiRenderer — Per-Channel Rendering

```csharp
public interface IUiRenderer
{
    object Render(UiDocument doc);
    bool SupportsInteractivity { get; }
    UiLayout[] SupportedLayouts { get; }
}
```

Renderers convert `UiDocument` to channel-native representations:
- **TelegramUiRenderer**: `(string text, InlineKeyboardMarkup markup)`
- **WebSocketUiRenderer**: JSON string with `{ type: "interactive", ... }`
- **TuiUiRenderer** (stub): Terminal.Gui View objects

### 3. Callback Routing

User actions (button taps, selections) flow back through:

```
Channel callback event
  → UiCallback parsed from channel-native format
  → CallbackRouter dispatches by Namespace
  → IUiCallbackHandler processes business logic
  → Returns UiDocument? (null = no UI change, just acknowledge)
```

```csharp
public sealed record UiCallback
{
    public string Namespace { get; init; } = "";
    public string Action { get; init; } = "";
    public string Data { get; init; } = "";
}

public interface IUiCallbackHandler
{
    string Namespace { get; }
    Task<UiDocument?> HandleAsync(UiCallback callback, IServiceProvider services, string agentId);
}
```

Callback data format: `{namespace}:{action}:{data}`  
Examples:  
- `model:browse` — open provider list  
- `model:list:fireworks` — list models for Fireworks (page 0)  
- `model:list:openrouter:2` — list models for OpenRouter, page 2  
- `model:select:fireworks/kimi` — switch to kimi  
- `model:reset` — clear override

### 4. IChannel Changes

```csharp
// Add to IChannel interface:
event Func<UiCallback, Task<UiDocument?>>? OnUiCallback;
Task<string?> SendInteractiveAsync(UiDocument doc);
Task EditInteractiveAsync(string messageId, UiDocument doc);
```

- `SendInteractiveAsync` returns the channel's message ID for later edits
- `EditInteractiveAsync` updates an existing interactive message in-place
- `OnUiCallback` is fired when user interacts (Telegram: callback_query, WebSocket: action message)

### 5. ModelSelectionHandler

```csharp
public class ModelSelectionHandler : IUiCallbackHandler
{
    public string Namespace => "model";

    // Actions:
    //   "browse"                → Screen 1: list all providers with model counts
    //   "list:{provider}"       → Screen 2: list models for a provider (paginated if >8)
    //   "select:{provider/model}" → Switch to model, persist, return updated Screen 2
    //   "reset"                 → Clear override, return to Screen 1 with default active
}
```

**Handler flow for each action:**

`browse` → Query `ProviderRouter.GetAvailableModels()`, group by provider, count models per provider. Build UiDocument with one UiSection per provider, each provider as a tappable UiItem.

`list:{provider}` → Filter models for provider. If count ≤ 8: single page. If > 8: page 0 of N. UiDocument with `Layout = Paged`, one UiSection per page, items = models for that page + navigation row.

`select:{provider/model}` → 
1. Validate model via `ProviderRouter.ResolveModelToProvider()`
2. Set `ProviderRouter.ModelChain` = `[selectedModel, ...existingFallbacks]`
3. Persist via `ConfigLoader.UpdateAgentModelAsync()`
4. Build new UiDocument for same provider's model list with `Selected` moved to new model
5. Return for in-place edit — user stays on Screen 2, sees `✅` move

`reset` → Clear `ModelChain` override, persist, return Screen 1 with default model active.

**No confirmation dialog** — model switch is immediate. If user taps wrong model, they tap again on the correct one.

### 6. Navigation Flow — Two-Level

`/model` opens a **two-level** navigation via inline keyboard. The message is edited in-place at each step — no new messages.

```
SCREEN 1: Provider List                    SCREEN 2: Model List
┌────────────────────────────────┐        ┌────────────────────────────────┐
│ 🧠 Models — Aria              │        │ 🔥 Fireworks AI — 3 models    │
│ Current: Kimi K2.6 Turbo      │        │ Current: Kimi K2.6 Turbo       │
│                                │        │                                │
│ Choose a provider:            │        │                                │
│                                │        │  ✅ Kimi K2.6 Turbo            │
│ 🔥 Fireworks AI  (3) ← active │  tap   │     Kimi K2.5 Turbo            │
│ 🌐 OpenRouter    (12)         │ ───→   │     Llama 4 Maverick           │
│ 🔷 Anthropic     (5)          │        │     DeepSeek V3                 │
│ 🧪 OpenAIO     (8)            │        │     ...                         │
│                                │        │                                │
│ [ 🔄 Reset to Default ]       │        │  ◀️ Back    ◀️ P1/2 ▶️        │
└────────────────────────────────┘        └────────────────────────────────┘
```

**Screen 1 — Provider List:**
- Each provider button shows: emoji + name + model count
- Provider with active model marked `← active` (or highlighted with `●`)
- "Reset to Default" button at bottom (only if user has an override set)
- Tapping a provider → edits message in-place to show Screen 2

**Screen 2 — Model List for Provider:**
- Header shows provider name + model count
- Current model marked with `✅`
- Tapping a non-current model → **immediate switch** (no confirmation), edits message to show Screen 2 with new `✅` position
- Tapping the current model → no-op (acknowledge only)
- `◀️ Back` returns to Screen 1 (provider list)
- Pagination row appears when provider has >8 models

### 7. Pagination

When a provider has more than 8 models, models are split into pages of 8. Pagination row appears between the model buttons and the Back button:

```
│  ✅ Kimi K2.6 Turbo            │
│     Kimi K2.5 Turbo            │
│     ... (6 more)               │
│                                │
│    ◀️ Page 1/3 ▶️              │
│                                │
│  ◀️ Back                       │
```

- Current page shown as `Page N/Total`
- `◀️` and `▶️` buttons navigate pages
- First page: `◀️` hidden or disabled (non-clickable `·` placeholder)
- Last page: `▶️` hidden or disabled
- Page navigation edits the message in-place (same message, new keyboard)
- Callback: `model:list:{provider}:{page}`

## File Plan

### New files (9)

| File | Purpose |
|------|---------|
| `src/Aether/Ui/UiDocument.cs` | UiDocument, UiSection, UiItem, UiLayout |
| `src/Aether/Ui/UiCallback.cs` | UiCallback record |
| `src/Aether/Ui/IUiRenderer.cs` | Renderer interface |
| `src/Aether/Ui/CallbackRouter.cs` | Callback dispatcher |
| `src/Aether/Ui/IUiCallbackHandler.cs` | Handler interface |
| `src/Aether/Ui/Handlers/ModelSelectionHandler.cs` | Model selection business logic |
| `src/Aether/Ui/Renderers/TelegramUiRenderer.cs` | UiDocument → Telegram InlineKeyboardMarkup |
| `src/Aether/Ui/Renderers/WebSocketUiRenderer.cs` | UiDocument → JSON |
| `src/Aether/Ui/Renderers/TuiUiRenderer.cs` | Stub for Terminal.Gui |

### Modified files (7)

| File | Changes |
|------|---------|
| `src/Aether/Channels/IChannel.cs` | Add `OnUiCallback` event, `SendInteractiveAsync`, `EditInteractiveAsync` |
| `src/Aether/Channels/TelegramChannel.cs` | Handle callback_query, implement interactive methods, parse callback data |
| `src/Aether/Channels/WebSocketChannel.cs` | Implement interactive methods (JSON), bridge action messages to callbacks |
| `src/Aether/Channels/NoOpChannel.cs` | Stub implementations |
| `src/Aether/Channels/ChannelMessageProcessor.cs` | Wire callback routing, pass UiDocument through renderer |
| `src/Aether/Channels/SlashCommandHandler.cs` | `/model`, `/models` return UiDocument instead of plain text |
| `src/Aether/Program.cs` | DI registration for Ui services |

## Testing

| Layer | What to test | How |
|-------|-------------|-----|
| UiDocument | Data model serialization, equality | Unit tests |
| TelegramUiRenderer | UiDocument → Markup conversion correctness | Unit tests (no bot needed) |
| CallbackRouter | Dispatch to correct handler by namespace | Unit tests |
| ModelSelectionHandler | Select, browse, list, reset logic | Unit tests with mock ProviderRouter |
| SlashCommandHandler | `/model`, `/model select X` → correct UiDocument shape | Unit tests |
| TelegramChannel | Callback parsing, event firing | Integration test with mock bot client |

## Non-Goals (explicitly excluded)

- Model parameter overrides (temperature, maxTokens) via UI — keep using text commands
- Provider health status in UI — future phase
- Fallback chain management via UI — future phase
- Agent switching via UI — separate command (`/agent`)
- Model selection in Avalonia Desktop app — that app is a separate assembly

## Rollout

1. **Phase 1** (this change): Core UiDocument model + Telegram renderer + ModelSelectionHandler
2. **Phase 2**: WebSocket renderer + TUI stub
3. **Phase 3**: Additional handlers (session management, memory browser, plugin settings)
