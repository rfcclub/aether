## 1. Dependency and Config

- [ ] 1.1 Add `Telegram.Bot` NuGet package to `src/Aether/Aether.csproj`
- [ ] 1.2 Add `TelegramOptions` record with `Token`, `Groups` (array) bound to `channels.telegram`
- [ ] 1.3 Add `TelegramGroupConfig` record with `ChatId`, `Name`, `Folder`, `Trigger?`
- [ ] 1.4 Extend `appsettings.json` `channels.telegram` with `groups` array (example entry with `chat_id`, `name`, `folder`, `trigger`)

## 2. TelegramChannel Implementation

- [ ] 2.1 Create `Channels/Telegram/TelegramChannel.cs` implementing `IChannel`
- [ ] 2.2 Inject `ITelegramBotClient` (constructed from `TelegramOptions.Token`)
- [ ] 2.3 Implement `ConnectAsync`: start `StartReceiving` long-polling loop, wire `OnMessage` event
- [ ] 2.4 Implement `DisconnectAsync`: cancel the polling loop
- [ ] 2.5 Implement `SendMessageAsync`: call `SendTextMessageAsync` (or `SendMessage` in newer API)
- [ ] 2.6 Implement `SetTypingAsync(true)`: call `SendChatActionAsync(ChatAction.Typing)`; `SetTypingAsync(false)` is no-op
- [ ] 2.7 Implement `OwnsChatId`: check if chatId matches any configured group
- [ ] 2.8 Normalize inbound `Update.Message` into `InboundMessage`; set `IsFromBot = true` for bot's own messages

## 3. Group Route Bootstrap

- [ ] 3.1 Add `AetherDb.UpsertGroupRouteAsync(GroupRoute route, CancellationToken ct)` method
- [ ] 3.2 In `AetherHostedService.StartAsync`, iterate `TelegramOptions.Groups` and call `UpsertGroupRouteAsync` for each

## 4. Hosted Service and Main Loop

- [ ] 4.1 Create `Hosting/AetherHostedService.cs` implementing `IHostedService`
- [ ] 4.2 `StartAsync`: call `TelegramChannel.ConnectAsync`, start message consumption background task
- [ ] 4.3 `StopAsync`: cancel background task, call `TelegramChannel.DisconnectAsync`
- [ ] 4.4 Consumption loop: dequeue from `IMessageQueue`, call `SetTypingAsync(true)`, call `AetherSoul.ProcessAsync`, call `SendMessageAsync`, call `SetTypingAsync(false)`
- [ ] 4.5 Add `ConcurrentDictionary<string, SemaphoreSlim>` for per-chat sequential gate
- [ ] 4.6 Wire error handling: catch exceptions per message, log error, continue loop

## 5. DI Wiring in Program.cs

- [ ] 5.1 Register `TelegramOptions` from config
- [ ] 5.2 Register `ITelegramBotClient` as singleton (constructed from token)
- [ ] 5.3 Register `TelegramChannel` as `IChannel` singleton
- [ ] 5.4 Register `AetherHostedService` as `IHostedService`

## 6. Tests

- [ ] 6.1 Smoke test: `InboundMessage` normalization from a mock Telegram `Message` object
- [ ] 6.2 Smoke test: group route bootstrap upserts rows into SQLite and is idempotent
- [ ] 6.3 Smoke test: `MessageRouter` routes a message from a known chat and ignores an unknown one
