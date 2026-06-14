# Intent: cron-kairos-scheduled-systems

## Raw Request

cron-kairos-scheduled-systems :
- CronSchedulerService: Đọc lịch từ ~/.aether/cron/*.md để tự động chạy backup, dọn dẹp, tổng hợp dữ liệu định kỳ.
- KAIROS Watch Service: Tự động phát hiện thay đổi trong file hệ thống và chủ động gửi thông báo (proactive notification) cho Agent xử lý.

## Problem

Aether lacks background task scheduling and proactive system notification mechanisms. Agents can only react to incoming user messages. There is no automated routine task runner (like a cron system) and no way to alert an agent of filesystem changes without the user initiating a chat.

## Desired Outcome

- Implement `CronSchedulerService` to load schedules from `~/.aether/cron/*.md` and execute periodic tasks like memory compaction, backups, and routine status checks.
- Implement the `KAIROS Watch Service` (`FileSystemWatcher`) that monitors critical files (like task inbox files) and proactively sends alerts/notifications to the agent.
- Parse YAML frontmatter of the markdown task files cleanly.

## Users / Actors

- The Aether Agents and the host system.

## Current Context

Currently, the agent is purely reactive. The only background service is `ChannelMessageProcessor` which waits for incoming channel messages.

## Proposed Direction

- Build `CronSchedulerService.cs` as a hosted service using the `Cronos` library.
- Build `KairosFileWatchService.cs` using `.NET FileSystemWatcher` to monitor filesystem changes.
- Implement a regex-based YAML frontmatter parser.

## Scope

- Core runtime scheduling and file watching features.
- Proactive event dispatching to the agent's channel processor.

## Non-Goals

- Complex user interfaces for task scheduling.
- Advanced calendar integrations.

## Constraints

- Scheduler must prevent task overlap (one run must complete before the next triggers).
- File watch notifications must implement a cooldown (e.g. 5 seconds) to prevent spamming the agent with multiple edits.

## Success Criteria

- Periodic tasks fire within 1 second of their scheduled boundary.
- File system modifications trigger proactive agent notifications.
- All unit tests compile and pass.

## Risks

- Performance degradation from constant file system monitoring or scheduling conflicts.
- Mitigation: Monitor only specific, crucial files rather than scanning entire deep workspace folders.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- None.

## Assumptions

- Task schedule configuration follows standard 5-field cron expression syntax.

## Spec Seeds

- Cron directory path `~/.aether/cron/`.
- YAML configuration parameters: `schedule`, `agent`, `channel`, `enabled`.

## Intent Approval

Status: APPROVED
Approved by: Thoor
Date: 2026-06-13
