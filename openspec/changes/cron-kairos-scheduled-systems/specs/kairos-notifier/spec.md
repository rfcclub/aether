## ADDED Requirements

### Requirement: KAIROS watches agent workspace for file changes
The system SHALL use `FileSystemWatcher` to monitor the agent workspace directory for changes to `.md` files matching configured watch patterns. When a change is detected, the system evaluates notification rules and pushes alerts via the bound channel.

#### Scenario: Watched file created
- **WHEN** a new file matching a KAIROS watch rule is created in the workspace (e.g., `research/findings-2026-05-03.md`)
- **THEN** the system sends a notification to the configured channel within 5 seconds

#### Scenario: Watched file modified
- **WHEN** an existing watched file is modified (e.g., `TASK_REPORT.md` appended)
- **THEN** the system sends a notification to the configured channel

#### Scenario: Unwatched file changed
- **WHEN** a file not matching any watch rule is modified (e.g., `SOUL.md`)
- **THEN** the system does not send a notification

### Requirement: KAIROS rules configured per-agent
The system SHALL read KAIROS notification rules from `{workspace}/.aether.json` under the `kairos.rules` key. Each rule SHALL specify: `watch` (glob pattern), `channel` (target channel name), and `cooldownSeconds` (minimum seconds between notifications for the same pattern).

#### Scenario: Agent has KAIROS rules
- **WHEN** `.aether.json` contains `"kairos": { "enabled": true, "rules": [{ "watch": "research/*.md", "channel": "telegram", "cooldownSeconds": 300 }] }`
- **THEN** the system watches for new/modified files matching `research/*.md` and notifies via Telegram at most once per 300 seconds

#### Scenario: KAIROS disabled
- **WHEN** `.aether.json` has `"kairos": { "enabled": false }` or no kairos section
- **THEN** the system does not watch files or send notifications for that agent

### Requirement: Cooldown prevents notification spam
The system SHALL enforce a per-rule cooldown period. After sending a notification for a rule, subsequent matching changes within `cooldownSeconds` SHALL be suppressed.

#### Scenario: Rapid file changes within cooldown
- **WHEN** a watched file is modified twice within 10 seconds and cooldown is 300 seconds
- **THEN** only the first change triggers a notification; the second is suppressed

#### Scenario: File change after cooldown expires
- **WHEN** a watched file is modified 301 seconds after the previous notification with cooldown 300
- **THEN** a new notification is sent

### Requirement: Notification sent directly via channel
KAIROS notifications SHALL be sent directly through the bound channel without LLM processing. The notification message SHALL include the file path, change type, and timestamp.

#### Scenario: Simple file creation notification
- **WHEN** a file `research/new-topic.md` is created matching a KAIROS rule
- **THEN** the system sends: "KAIROS: research/new-topic.md created [2026-05-03 09:00:00]" via the configured channel

### Requirement: KAIROS runs as BackgroundService
The KAIROS watch service SHALL be registered as an `IHostedService` and start monitoring at application startup. It SHALL stop all watchers on application shutdown.

#### Scenario: Service starts with application
- **WHEN** the Aether host starts
- **THEN** KAIROS begins watching agent workspaces within 1 second
