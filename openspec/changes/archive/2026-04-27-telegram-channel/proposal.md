## Why

`IChannel` and `InboundMessage` are defined but no implementation exists. Without `TelegramChannel`, Aether cannot receive messages from or reply to Telegram. This is the only inbound channel planned for Phase 1 — without it, the agent is unreachable from the real world.

## What Changes

- Add `Channels/Telegram/TelegramChannel.cs` implementing `IChannel` using `Telegram.Bot`
- Add `TelegramOptions` for bot token and optional allowed user IDs
- Wire `TelegramChannel` and `MessageRouter` into a hosted `IHostedService` that connects on startup, routes inbound messages, processes them through `AetherSoul`, and sends replies
- Add group route bootstrap: load `appsettings.json` `channels.telegram.groups` array and upsert into SQLite `groups` table on startup
- Add `Telegram.Bot` NuGet package

## Capabilities

### New Capabilities

- `telegram-channel`: Telegram Bot API channel implementation with receive/send/typing support
- `group-route-bootstrap`: Config-driven group route registration into SQLite on startup

### Modified Capabilities

- (none)

## Impact

- **New files**: `Channels/Telegram/TelegramChannel.cs`, `Channels/Telegram/TelegramOptions.cs`, `Hosting/AetherHostedService.cs`
- **NuGet**: `Telegram.Bot` (latest stable)
- **Config**: `channels.telegram.token`, `channels.telegram.groups` array in `appsettings.json`
- **Modified**: `Program.cs` — register channel, hosted service, wire DI
