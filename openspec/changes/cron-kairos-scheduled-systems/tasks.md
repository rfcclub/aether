## 1. NuGet and Project Setup

- [ ] 1.1 Add `Cronos` NuGet package to `Aether.csproj` for cron expression parsing
- [ ] 1.2 Create `src/Aether/Scheduling/` namespace directory

## 2. Cron Task Definition and Parser

- [ ] 2.1 Create `CronTaskDefinition.cs` — record: Schedule (string), Agent (string), Channel (string), Enabled (bool), Body (string), FilePath (string)
- [ ] 2.2 Create `CronFrontmatterParser.cs` — static method `ParseAsync(string filePath)` that regex-extracts YAML frontmatter and returns `CronTaskDefinition`
- [ ] 2.3 Write unit test: well-formed frontmatter parses correctly
- [ ] 2.4 Write unit test: missing frontmatter fallback to defaults (hourly schedule)
- [ ] 2.5 Write unit test: `enabled: false` returns null/skipped task

## 3. Cron Scheduler Service

- [ ] 3.1 Create `CronSchedulerService.cs` — `BackgroundService` that reads `~/.aether/cron/*.md`, parses tasks, creates timers via Cronos
- [ ] 3.2 Implement `LoadTasksAsync()` — scan directory, parse each file, return enabled tasks
- [ ] 3.3 Implement `ScheduleTask(CronTaskDefinition)` — calculate next occurrence with Cronos, create `Timer`
- [ ] 3.4 Implement `ExecuteTaskAsync(CronTaskDefinition, CancellationToken)` — send body to `AetherSoul.ProcessAsync()`, route output to channel
- [ ] 3.5 Implement overlap guard — skip if previous execution still running
- [ ] 3.6 Implement minimum interval enforcement (60 seconds)
- [ ] 3.7 Register `CronSchedulerService` as `IHostedService` in `Program.cs`

## 4. Cron Integration Tests

- [ ] 4.1 Write test: task fires on schedule (use short interval, verify execution)
- [ ] 4.2 Write test: disabled task is skipped
- [ ] 4.3 Write test: overlap prevented
- [ ] 4.4 Write test: output containing "HEARTBEAT_OK" is suppressed
- [ ] 4.5 Write test: output without "HEARTBEAT_OK" is sent to channel

## 5. KAIROS Rule Model and Config

- [ ] 5.1 Create `KairosRule.cs` — record: Watch (string glob), Channel (string), CooldownSeconds (int)
- [ ] 5.2 Create `KairosConfig.cs` — record: Enabled (bool), Rules (List<KairosRule>)
- [ ] 5.3 Add `KairosConfig?` to `AgentSpecConfig` (SpecContracts.cs)
- [ ] 5.4 Update `ConfigLoader` to parse `kairos` section from `.aether.json`

## 6. KAIROS Watch Service

- [ ] 6.1 Create `KairosWatchService.cs` — `BackgroundService` using `FileSystemWatcher` per agent workspace
- [ ] 6.2 Implement `StartWatching(AgentProfile, KairosConfig)` — create FileSystemWatcher for workspace, filter by glob patterns
- [ ] 6.3 Implement `OnFileChanged(object, FileSystemEventArgs)` — check against rules, apply cooldown, send notification
- [ ] 6.4 Implement cooldown tracker — `Dictionary<string, DateTime>` keyed by rule watch pattern
- [ ] 6.5 Send notification via `IChannel.SendMessageAsync()` with format: "KAIROS: {relativePath} {changeType} [{timestamp}]"
- [ ] 6.6 Handle FileSystemWatcher error/buffer overflow events — log and reinitialize
- [ ] 6.7 Register `KairosWatchService` as `IHostedService` in `Program.cs`

## 7. KAIROS Integration Tests

- [ ] 7.1 Write test: file creation in watched path triggers notification
- [ ] 7.2 Write test: file modification outside watch pattern does NOT trigger
- [ ] 7.3 Write test: cooldown suppresses rapid duplicate notifications
- [ ] 7.4 Write test: KAIROS disabled does not watch files

## 8. Agent Scaffold Update

- [ ] 8.1 Update `AgentWorkspaceScaffolder` to generate sample `cron/daily-check.md` if it doesn't exist
- [ ] 8.2 Update scaffolded `.aether.json` template to include `kairos` section (disabled by default)

## 9. Build and Verify

- [ ] 9.1 `dotnet build` — zero errors
- [ ] 9.2 `dotnet test` — all existing + new tests pass
- [ ] 9.3 Smoke test: create a cron task file, start Aether, verify it fires
- [ ] 9.4 Smoke test: create a file matching KAIROS watch pattern, verify notification sent
