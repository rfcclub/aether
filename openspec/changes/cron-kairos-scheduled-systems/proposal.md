## Why

Aether has a single fixed heartbeat (`AgentHeartbeatService` every 5 min reading HEARTBEAT.md) but no general scheduling system. Agents need cron-style recurring tasks (morning digest, hourly checks, PR watchers) and proactive notifications (KAIROS) when important state changes — research findings, task completions, errors. Without these, agents are purely reactive: they only respond to inbound messages. They can't initiate.

## What Changes

- **Cron scheduler**: `CronSchedulerService` (BackgroundService) reads `~/.aether/cron/*.md` files with YAML frontmatter (`schedule`, `agent`, `channel`, `enabled`). Each file defines a recurring task. On schedule fire: sends task prompt through `AetherSoul.ProcessAsync()`, routes output to the specified channel.
- **KAIROS proactive notifier**: `KairosWatchService` (BackgroundService) watches agent workspace for file changes (`FileSystemWatcher`), evaluates notification rules, pushes alerts via bound channel. Rules defined per-agent in `.aether.json` under `kairos.rules`.
- **Cronos NuGet** for cron expression parsing (lightweight, no dependencies).
- **YAML frontmatter parsing** for cron task files (add `YamlDotNet` or use simple regex parser).

## Capabilities

### New Capabilities
- `cron-scheduler`: Recurring scheduled task engine reading cron definitions from `~/.aether/cron/*.md`, executing via AetherSoul
- `kairos-notifier`: Proactive file-watch notification system detecting workspace changes and pushing alerts via channels

## Impact

- **New files**: `src/Aether/Scheduling/CronSchedulerService.cs`, `src/Aether/Scheduling/CronTaskDefinition.cs`, `src/Aether/Scheduling/KairosWatchService.cs`, `src/Aether/Scheduling/KairosRule.cs`
- **NuGet**: `Cronos` (cron expression parsing)
- **Config**: `.aether.json` gains optional `kairos` section; `~/.aether/cron/` populated with task files
- **DI**: Two new `IHostedService` registrations in `Program.cs`
- **Agent scaffold**: `AgentWorkspaceScaffolder` gains optional Kairos config template
