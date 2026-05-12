## ADDED Requirements

### Requirement: UiDocument data model

The system SHALL provide a channel-agnostic `UiDocument` record that describes interactive UI content without referencing any specific channel or rendering technology.

A `UiDocument` SHALL contain:
- `Text`: main display text (supports MarkdownV2)
- `Sections`: ordered list of `UiSection` groups, each with a `Title`, optional `Subtitle`, and ordered list of `UiItem`
- `Layout`: enum hint (`List`, `Grid`, `Paged`) for the renderer
- `CallbackNamespace`: string prefix for routing user interactions back to the correct handler

Each `UiItem` SHALL contain:
- `Id`: unique identifier within its section
- `Label`: display text
- Optional `Emoji`, `Selected` flag, and `StatusBadge`

#### Scenario: UiDocument is channel-agnostic

- **WHEN** a command handler creates a `UiDocument` with 3 sections and 5 items
- **THEN** the document SHALL contain no Telegram-specific markup, JSON, or rendering instructions
- **AND** the same document SHALL be renderable by any `IUiRenderer` implementation

#### Scenario: UiItem carries selection state

- **WHEN** a model is currently active
- **THEN** its `UiItem` SHALL have `Selected = true`
- **AND** the renderer SHALL visually distinguish it (e.g., `✅` prefix on Telegram)

### Requirement: IUiRenderer per-channel rendering

The system SHALL provide an `IUiRenderer` interface that converts `UiDocument` to channel-native representations.

Each renderer SHALL declare:
- `SupportsInteractivity`: whether the channel supports callbacks (false for plain text channels)
- `SupportedLayouts`: which `UiLayout` values it can honor

#### Scenario: Telegram renderer produces inline keyboard markup

- **WHEN** `TelegramUiRenderer.Render(doc)` is called with a UiDocument containing 2 sections with 3 items each
- **THEN** the result SHALL be a tuple of (text, InlineKeyboardMarkup)
- **AND** each section SHALL render as a row group in the keyboard
- **AND** each item SHALL render as a button with its Label text

#### Scenario: WebSocket renderer produces JSON

- **WHEN** `WebSocketUiRenderer.Render(doc)` is called
- **THEN** the result SHALL be a JSON string with `type: "interactive"`, `sections`, and `actions` fields

### Requirement: CallbackRouter dispatches user interactions

The system SHALL provide a `CallbackRouter` that routes `UiCallback` objects to registered `IUiCallbackHandler` implementations based on the callback's `Namespace`.

A `UiCallback` SHALL contain:
- `Namespace`: routing key (e.g., `"model"`)
- `Action`: verb (e.g., `"select"`, `"browse"`)
- `Data`: payload (e.g., `"fireworks/kimi"`)

#### Scenario: Callback routed to correct handler

- **WHEN** `CallbackRouter.RouteAsync(UiCallback { Namespace = "model", Action = "select", Data = "fireworks/kimi" })` is called
- **THEN** the registered `ModelSelectionHandler` SHALL receive the callback
- **AND** its `HandleAsync` method SHALL be invoked

#### Scenario: Unknown namespace returns null

- **WHEN** `CallbackRouter.RouteAsync(UiCallback { Namespace = "unknown" })` is called
- **THEN** the method SHALL return `null` without throwing
- **AND** the channel SHALL acknowledge the callback with no UI change

### Requirement: IUiCallbackHandler interface

The system SHALL define `IUiCallbackHandler` with:
- `Namespace`: the callback namespace this handler owns
- `HandleAsync(UiCallback, IServiceProvider, string agentId)`: process the callback and return an optional `UiDocument`

#### Scenario: Handler returns updated UiDocument

- **WHEN** a handler successfully processes a callback and wants to update the UI
- **THEN** it SHALL return a new `UiDocument`
- **AND** the channel SHALL edit the existing interactive message in-place

#### Scenario: Handler returns null

- **WHEN** a handler processes a callback but wants no UI change (e.g., current model tapped again)
- **THEN** it SHALL return `null`
- **AND** the channel SHALL acknowledge the callback without editing the message
