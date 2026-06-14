## Why

Model selection on Telegram is currently text-only — users must type exact model IDs like `/model fireworks/kimi-k2p6-turbo` to switch. There's no visual grouping by provider, no tap-to-select, and no indication of which model is active beyond a `<- current` text marker. This makes model switching slow and error-prone, especially on mobile. Making it interactive with native Telegram controls turns a 20-character typed command into a single tap.

## What Changes

- New `UiDocument` data model — channel-agnostic representation of interactive UI (sections, items, actions, layout hints)
- New `IUiRenderer` interface — per-channel renderers that convert UiDocument to native formats
- New `CallbackRouter` + `IUiCallbackHandler` — dispatches user interactions (button taps) to business logic handlers
- New `ModelSelectionHandler` — two-level navigation (provider list → model list) with pagination and one-tap switching
- **Modified** `IChannel` — adds `OnUiCallback` event, `SendInteractiveAsync`, `EditInteractiveAsync`
- **Modified** `TelegramChannel` — handles `callback_query` events, renders inline keyboards, bridges callbacks to the router
- **Modified** `WebSocketChannel` — JSON interactive messages, action-based callbacks
- **Modified** `SlashCommandHandler` — `/model` and `/models` return UiDocument instead of plain text

## Capabilities

### New Capabilities

- `interactive-ui`: Channel-agnostic UiDocument data model, IUiRenderer per-channel rendering, CallbackRouter dispatch, and IUiCallbackHandler interface. This is the foundation — all future interactive slash commands reuse this infrastructure.
- `model-selection-handler`: Two-level interactive model browsing (provider list → paginated model list), one-tap model switching with config persistence, reset-to-default. Callback namespace `model`.

### Modified Capabilities

- `telegram-channel`: Adds callback_query handling, SendInteractiveAsync/EditInteractiveAsync with inline keyboard rendering, and OnUiCallback event bridging. Existing text-only SendMessageAsync remains unchanged.

## Impact

- **New code**: `src/Aether/Ui/` (9 files — data model, renderers, router, handler)
- **Modified code**: `IChannel.cs`, `TelegramChannel.cs`, `WebSocketChannel.cs`, `NoOpChannel.cs`, `ChannelMessageProcessor.cs`, `SlashCommandHandler.cs`, `Program.cs`
- **Dependencies**: No new NuGet packages — Telegram.Bot already supports inline keyboards and callback queries
- **Breaking changes**: None — `IChannel` additions are default-implemented, existing text commands still work
- **Tests**: Unit tests for UiDocument model, TelegramUiRenderer, CallbackRouter, ModelSelectionHandler, SlashCommandHandler
