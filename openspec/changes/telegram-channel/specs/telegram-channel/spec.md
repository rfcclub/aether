## ADDED Requirements

### Requirement: Connect and Receive

`TelegramChannel` MUST start long-polling on `ConnectAsync` and raise `OnMessage` for each received text message.

#### Scenario: Text message received
- **WHEN** a Telegram user sends a text message to the bot
- **THEN** `OnMessage` is raised with an `InboundMessage` containing `ChatId`, `SenderId`, `Text`, and `Timestamp`

#### Scenario: Non-text updates ignored
- **WHEN** a Telegram update arrives with no text (e.g. sticker, photo, join event)
- **THEN** `OnMessage` is NOT raised

#### Scenario: Bot messages ignored
- **WHEN** the received message was sent by the bot itself
- **THEN** `InboundMessage.IsFromBot = true`

### Requirement: Send Message

`TelegramChannel.SendMessageAsync` MUST deliver text to the specified Telegram chat.

#### Scenario: Message sent
- **WHEN** `SendMessageAsync(chatId, text, ct)` is called
- **THEN** the text is sent to the Telegram chat via Bot API

### Requirement: Typing Indicator

`TelegramChannel.SetTypingAsync` MUST send a typing chat action.

#### Scenario: Typing action sent
- **WHEN** `SetTypingAsync(chatId, true, ct)` is called
- **THEN** `ChatAction.Typing` is sent to the Telegram chat

#### Scenario: Typing cleared (no-op)
- **WHEN** `SetTypingAsync(chatId, false, ct)` is called
- **THEN** no action is needed (Telegram clears typing automatically)

### Requirement: Hosted Service Drives the Loop

`AetherHostedService` MUST wire the channel, router, queue, and soul together.

#### Scenario: Message is processed and replied
- **WHEN** an `InboundMessage` is routed to a known group and dequeued from `IMessageQueue`
- **THEN** typing is set, `AetherSoul.ProcessAsync` is called, and the response is sent back to the originating `ChatId`

#### Scenario: Unknown group is silently dropped
- **WHEN** `MessageRouter.RouteAsync` returns null (chat not in groups table)
- **THEN** no reply is sent and no error is logged at warning level or above

### Requirement: Per-Chat Sequential Processing

Messages from the same chat MUST be processed sequentially.

#### Scenario: Two rapid messages from same chat
- **WHEN** two messages arrive from the same `ChatId` before the first is processed
- **THEN** the second waits for the first to complete before `AetherSoul.ProcessAsync` is called
