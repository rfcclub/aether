# group-route-bootstrap Specification

## Purpose
TBD - created by archiving change telegram-channel. Update Purpose after archive.
## Requirements
### Requirement: Config-Driven Group Registration

On startup, the system MUST upsert all groups defined in `channels.telegram.groups` into the `groups` SQLite table.

#### Scenario: Group registered from config
- **WHEN** `AetherHostedService.StartAsync` runs and `channels.telegram.groups` contains one or more group entries
- **THEN** each entry is upserted into `groups` with its `chat_id`, `name`, `folder`, and optional `trigger`

#### Scenario: Idempotent upsert
- **WHEN** `StartAsync` is called multiple times (e.g. restart)
- **THEN** existing rows are updated in-place (not duplicated)

### Requirement: Group Config Schema

Each group entry in `channels.telegram.groups` MUST support:
- `chat_id` (string): Telegram chat ID used as the `jid` / route key
- `name` (string): Human-readable group name
- `folder` (string): Local filesystem folder for memory and sessions
- `trigger` (string, optional): Message prefix required to trigger the agent; if absent, all messages trigger

#### Scenario: Group without trigger responds to all messages
- **WHEN** a group has no `trigger` configured
- **THEN** `MessageRouter` routes all non-bot messages from that chat to the queue

#### Scenario: Group with trigger filters messages
- **WHEN** a group has `trigger: "@Aether"` configured
- **THEN** only messages starting with `"@Aether"` are routed; the trigger prefix is stripped before processing

