## Context

`IChannel` defines `ConnectAsync`, `DisconnectAsync`, `SendMessageAsync`, `SetTypingAsync`, `OnMessage`, `OwnsChatId`. `MessageRouter` already handles routing inbound messages to the queue. `AetherDb` has a `groups` table and `GetGroupRouteAsync`/`RegisterGroupRouteAsync` methods. `appsettings.json` has `channels.telegram` with `enabled` and `bot_token`.

## Goals / Non-Goals

**Goals:**
- `TelegramChannel` implements `IChannel` using `Telegram.Bot` long-polling (no webhook for simplicity)
- `AetherHostedService` starts the channel, listens for messages, routes via `MessageRouter`, dequeues from `IMessageQueue`, calls `AetherSoul`, sends reply
- Group route bootstrap: config `channels.telegram.groups` array → upsert into `groups` SQLite table at startup
- Typing indicator: `SetTypingAsync` sends `SendChatActionAsync(ChatAction.Typing)`

**Non-Goals:**
- Webhook mode (use long-polling only in Phase 1)
- Media messages (text only)
- Multiple Telegram bots / accounts

## Decisions

**D1 — Long-polling via `ITelegramBotClient.ReceiveAsync`**: Use the receiver pattern from `Telegram.Bot.Extensions.Polling` (or the built-in `StartReceiving` in recent versions). Runs in the hosted service lifetime.

**D2 — Hosted service owns the main loop**: `AetherHostedService : IHostedService` starts channel → starts message consumption loop → on each `RoutedMessage` dequeued, calls `AetherSoul.ProcessAsync` → sends reply → sends typing action before call. Runs concurrently with channel event handler.

**D3 — Per-chat sequential processing**: Process one message per chatId at a time using a `ConcurrentDictionary<string, SemaphoreSlim>` to prevent overlapping replies to the same chat.

**D4 — Group config bootstrap**: Add `channels.telegram.groups` as an array of objects with `chat_id`, `name`, `folder`, `trigger` (optional). On `StartAsync`, upsert each into `groups` table via `AetherDb`. This eliminates the need for a separate DB admin step.

**D5 — `bot_token` from env**: `${TELEGRAM_BOT_TOKEN}` is already in config. `IConfiguration` resolves env vars automatically via .NET host. No special handling needed.

## Risks / Trade-offs

- **Long-polling vs webhook**: Long-polling is simpler to deploy but creates one persistent HTTP connection. Acceptable for Phase 1; webhook can be added later.
- **Sequential per-chat**: Correct and simple. Parallel per-chat is possible but adds complexity without clear Phase 1 need.
- **Missing `Telegram.Bot.Extensions.Polling`**: In `Telegram.Bot` ≥ 19.x, polling is built-in via `StartReceiving`. Use the package version's native API.
