## Context

Aether currently has text-only slash commands (`/model`, `/models`) that return plain text via `SendMessageAsync`. Telegram supports rich interactive controls — inline keyboards, callback queries, message editing — that can make model selection a single-tap operation. This design establishes a channel-agnostic UI abstraction so all future interactive commands (session management, memory browser, plugin settings) can reuse the same infrastructure.

The existing `TelegramChannel` already uses the `Telegram.Bot` library which supports `InlineKeyboardMarkup` and `OnCallbackQuery`. The existing `ProviderRouter.GetAvailableModels()` already enumerates all provider/model pairs. The existing `ConfigLoader.UpdateAgentModelAsync()` already persists model changes. What's missing is the connection layer: turning a command handler's output into interactive buttons, and routing button taps back to business logic.

## Goals / Non-Goals

**Goals:**
- UiDocument: a pure data model describing interactive content (text, sections, items, layout hints)
- IUiRenderer: per-channel rendering — Telegram gets inline keyboards, WebSocket gets JSON, TUI gets views
- CallbackRouter: dispatches user interactions by namespace to registered handlers
- ModelSelectionHandler: two-level provider → model navigation, pagination (>8 models), one-tap switch with persistence
- In-place message editing: model switch edits the existing message, no new message spam
- Zero breaking changes to IChannel: new members are default-implemented

**Non-Goals:**
- Model parameter overrides (temperature, maxTokens) via UI
- Provider health status badges in UI (data model supports it, but not rendered yet)
- Fallback chain management via UI
- Agent switching via UI
- Model selection in Avalonia Desktop app (separate assembly)

## Decisions

### Decision 1: UiDocument as data, not markup

**Chosen**: UiDocument is a pure C# record with no rendering logic. Each channel gets its own `IUiRenderer` implementation.

**Alternatives considered**:
- Generate Telegram-specific markup directly in the command handler → rejected because it couples commands to Telegram and makes WebSocket/TUI impossible
- Use HTML as intermediate format → rejected because HTML doesn't map cleanly to Telegram's button row/column model nor to Terminal.Gui views

**Rationale**: The UiDocument model is intentionally constrained — sections contain items, items have labels and optional actions. This is sufficient for all current interactive use cases (lists, selects, navigation) without over-engineering. If future use cases need richer controls (text inputs, date pickers), we can extend UiDocument without breaking existing renderers.

### Decision 2: Callback data as compact string

**Chosen**: `namespace:action:data` format (e.g. `model:select:fireworks/kimi`), fit within Telegram's 64-byte callback_data limit.

**Alternatives considered**:
- JSON callback data → rejected because Telegram limits callback_data to 64 bytes; JSON overhead would consume most of that
- Numeric IDs with server-side state lookup → rejected because it requires session-scoped state management and complicates multi-device scenarios

**Rationale**: The namespace prefix enables routing without a central registry lookup. Action + data is sufficient for model IDs, provider names, and page numbers. The `/` in model IDs is safe because we split only on the first two `:` characters.

### Decision 3: In-place message editing over new messages

**Chosen**: Model selection navigates by editing the existing Telegram message (`EditMessageText` + `EditMessageReplyMarkup`).

**Alternatives considered**:
- New message per screen → rejected because it spams the chat and loses navigation context
- Popup/webapp → rejected because Telegram mini-apps are overkill for a list picker

**Rationale**: In-place editing keeps the chat clean. The user's `/model` command and the interactive UI are a single message that updates as they navigate. This is the standard Telegram bot UX pattern for settings/configuration flows.

### Decision 4: No confirmation on model switch

**Chosen**: Tapping a model switches immediately. No "Are you sure?" prompt.

**Rationale**: Model switching is low-stakes — the user's conversation context is preserved, only the next LLM call uses the new model. If they tap wrong, they tap again. A confirmation dialog adds friction without preventing meaningful errors.

## Risks / Trade-offs

- **Telegram callback_data 64-byte limit**: Model IDs like `accounts/fireworks/routers/kimi-k2p6-turbo` are ~50 chars, leaving ~14 chars for namespace + action delimiter. Mitigation: use short namespace prefix (`mdl` instead of `model`) if needed. Currently `model:select:` = 14 chars + model ID fits within 64 bytes.
- **Callback handler resolution at runtime**: Handlers are resolved from DI per-callback. If a handler throws, the callback is acknowledged with a generic error text rather than crashing the poll loop. Mitigation: CallbackRouter wraps handler calls in try/catch.
- **Pagination state is stateless**: Page number is in the callback data, not server-side session state. If two users share a provider list, they paginate independently. No risk of cross-user state leakage.
- **Message not found on edit**: If user deletes the interactive message before tapping a button, `EditMessageText` returns 404. Mitigation: catch `ApiRequestException`, log debug, send a new message instead.

## Open Questions

None — all decisions resolved in this design.
