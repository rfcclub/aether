## ADDED Requirements

### Requirement: Callback query handling

`TelegramChannel` SHALL register a `OnCallbackQuery` handler that parses callback data into `UiCallback` and fires the `OnUiCallback` event.

The callback data format SHALL be `namespace:action:data` where:
- `namespace` maps to an `IUiCallbackHandler` registration
- `action` is the handler-specific verb
- `data` is the handler-specific payload

#### Scenario: Callback query fires OnUiCallback

- **WHEN** a Telegram user taps an inline keyboard button with callback data `model:select:fireworks/kimi`
- **THEN** `TelegramChannel` SHALL parse it into `UiCallback { Namespace = "model", Action = "select", Data = "fireworks/kimi" }`
- **AND** fire `OnUiCallback` with that callback
- **AND** call `AnswerCallbackQueryAsync` to acknowledge the tap

#### Scenario: Callback from unknown chat is ignored

- **WHEN** a callback query arrives from a chat not in the access allowlist
- **THEN** `AnswerCallbackQueryAsync` SHALL be called with an access-denied text
- **AND** `OnUiCallback` SHALL NOT fire

### Requirement: Send interactive message

`TelegramChannel.SendInteractiveAsync` SHALL render a `UiDocument` via `TelegramUiRenderer` and send it to the specified chat with an inline keyboard.

#### Scenario: Interactive message sent

- **WHEN** `SendInteractiveAsync(doc)` is called with a UiDocument containing 2 sections
- **THEN** the message text SHALL be sent via `SendTextMessageAsync` with `replyMarkup` containing the rendered inline keyboard
- **AND** the method SHALL return the Telegram message ID string

### Requirement: Edit interactive message

`TelegramChannel.EditInteractiveAsync` SHALL update an existing message's text and inline keyboard.

#### Scenario: Interactive message edited in-place

- **WHEN** `EditInteractiveAsync("42", newDoc)` is called after a model switch
- **THEN** `EditMessageTextAsync` SHALL update the message text
- **AND** `EditMessageReplyMarkupAsync` SHALL update the inline keyboard
- **AND** the message SHALL remain in its original position in the chat

#### Scenario: Edit fails gracefully on deleted message

- **WHEN** `EditInteractiveAsync` is called but the message has been deleted by the user
- **THEN** a new message SHALL be sent via `SendInteractiveAsync` instead
- **AND** no exception SHALL propagate to the caller
