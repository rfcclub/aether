## Context

Aether has 7+ `IHostedService`/`BackgroundService` implementations (`AgentHeartbeatService`, `ChannelMessageProcessor`, `WebSocketChannelService`, `DailyReviewHostedService`, `ProviderHealthMonitor`, `ToolHotReloadService`, `ToolStartupRegistration`). Adding cron and KAIROS follows the same pattern — each is a long-lived background service managed by the .NET Generic Host.

Current heartbeat is limited: one task, one interval, one file (HEARTBEAT.md). Cron generalizes this to N tasks with arbitrary schedules defined as files in `~/.aether/cron/`. KAIROS is different — reactive, not scheduled — watching for file changes and pushing notifications without an LLM round-trip.

## Goals / Non-Goals

**Goals:**
- Cron: read `~/.aether/cron/*.md`, parse YAML frontmatter for schedule metadata, fire tasks on schedule through AetherSoul, deliver output to bound channel
- Kairos: watch agent workspace for file changes, evaluate notification rules, push alerts via channel without LLM processing
- Use `Cronos` library for cron expression parsing (proven, zero-dependency)
- Simple YAML frontmatter parsing (regex-based, no heavy YAML library needed for 4 fields)

**Non-Goals:**
- Distributed cron (single-instance only, same as heartbeat)
- Kairos with LLM evaluation (v1 is rule-based, file-change → notify directly)
- Cron task editing UI (files, like everything else in Aether)
- Persistent task run history beyond channel messages (logs go to journal)

## Decisions

### Decision 1: Cronos over NCrontab or manual parsing

**Chosen**: `Cronos` — handles all 5-field cron expressions including `*/15`, ranges, comma lists. Used by Hangfire (proven). Single file, no dependencies.

### Decision 2: Regex YAML frontmatter over YamlDotNet

**Chosen**: Simple regex `^---\s*\n(.*?)\n---` with manual key-value parsing. Cron task files have exactly 4 metadata fields (`schedule`, `agent`, `channel`, `enabled`). No nested structures. A full YAML library is overkill.

### Decision 3: Kairos = FileSystemWatcher, not polling

**Chosen**: `FileSystemWatcher` on agent workspace directory. More responsive than polling, lower CPU. Only watches `*.md` files in the workspace root (research findings, task reports, error logs).

**Alternative**: Timer-based polling every 30s. Rejected — unnecessary CPU when nothing changes.

### Decision 4: Kairos rules in .aether.json

**Chosen**: Per-agent config under `kairos.rules` array. Each rule specifies: `watch` (glob pattern), `channel` (where to notify), `cooldownSeconds` (avoid spam). Example:
```json
{
  "kairos": {
    "enabled": true,
    "rules": [
      { "watch": "research/*.md", "channel": "telegram", "cooldownSeconds": 300 },
      { "watch": "TASK_REPORT.md", "channel": "telegram", "cooldownSeconds": 60 }
    ]
  }
}
```

### Decision 5: Cron task file format

```markdown
---
schedule: "0 9 * * *"
agent: default
channel: telegram
enabled: true
---
Check TASK_INBOX.md and HEARTBEAT.md. Report pending tasks to Thoor.
Keep it brief — one sentence per task.
```

Body is the prompt sent to AetherSoul. Schedule is standard 5-field cron (minute hour day month weekday).

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   Aether Host                            │
│                                                         │
│  ┌──────────────────┐  ┌──────────────────┐            │
│  │ CronScheduler    │  │ KairosWatch      │            │
│  │ Service          │  │ Service          │            │
│  │                  │  │                  │            │
│  │ Read cron/*.md   │  │ FileSystemWatcher│            │
│  │ Parse YAML front │  │ Evaluate rules   │            │
│  │ Cronos → Timer   │  │ Cooldown guard   │            │
│  └────────┬─────────┘  └────────┬─────────┘            │
│           │                     │                       │
│           ▼                     │                       │
│  ┌──────────────────┐           │                       │
│  │   AetherSoul     │           │                       │
│  │   ProcessAsync   │           │                       │
│  └────────┬─────────┘           │                       │
│           │                     │                       │
│           └──────────┬──────────┘                       │
│                      ▼                                  │
│           ┌──────────────────┐                          │
│           │  Channel.SendMsg │                          │
│           │  (Telegram etc)  │                          │
│           └──────────────────┘                          │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

## Risks / Trade-offs

- **FileSystemWatcher reliability on Linux**: `FileSystemWatcher` uses inotify, which has limits on number of watches. → Mitigation: watch only workspace root, not deep subtrees. Set `InternalBufferSize` appropriately.
- **Cron task spam**: A misconfigured cron task could fire every minute and flood the channel. → Mitigation: minimum interval of 60 seconds enforced in code. Warn on shorter schedules.
- **Task overlap**: Long-running cron task could overlap with next scheduled run. → Mitigation: skip if previous run still executing (simple `_running` flag).
