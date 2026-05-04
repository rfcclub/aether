## ADDED Requirements

### Requirement: Cron tasks loaded from directory
The system SHALL read all `.md` files from `~/.aether/cron/` at startup and reload on file change. Each file SHALL contain YAML frontmatter with `schedule`, `agent`, `channel`, and optional `enabled` fields, followed by a markdown body used as the task prompt.

#### Scenario: Cron directory with task files
- **WHEN** `~/.aether/cron/` contains `morning-check.md` with frontmatter `schedule: "0 9 * * *"`, `agent: default`, `channel: telegram`, `enabled: true`
- **THEN** the system parses this as an enabled cron task scheduled daily at 9:00 AM

#### Scenario: Task file without schedule
- **WHEN** a cron file has no `schedule` field in frontmatter
- **THEN** the system logs a warning and skips the file

#### Scenario: Task file with enabled: false
- **WHEN** a cron file has `enabled: false`
- **THEN** the system skips the task without logging an error

#### Scenario: Empty cron directory
- **WHEN** `~/.aether/cron/` is empty
- **THEN** the system starts with zero scheduled tasks and logs an info message

### Requirement: Cron tasks execute on schedule
The system SHALL parse the `schedule` field as a standard 5-field cron expression (minute hour day month weekday) and fire the task at the next matching time.

#### Scenario: Task fires at scheduled time
- **WHEN** a task has schedule `"*/30 * * * *"` (every 30 minutes)
- **THEN** the task fires within 1 second of each 30-minute boundary

#### Scenario: Minimum interval enforcement
- **WHEN** a task has schedule `"* * * * *"` (every minute)
- **THEN** the system enforces a minimum 60-second interval and logs a warning

#### Scenario: Task overlap prevented
- **WHEN** a task is still executing from its previous run
- **THEN** the scheduler skips the next scheduled fire and logs a debug message

### Requirement: Cron task executes through AetherSoul
On schedule fire, the system SHALL send the task's markdown body as a prompt to `AetherSoul.ProcessAsync()` using the agent specified in the task's frontmatter.

#### Scenario: Task prompt processed by agent
- **WHEN** a cron task fires with body "Check TASK_INBOX.md" and agent is "default"
- **THEN** the scheduler calls `AetherSoul.ProcessAsync("default", "Check TASK_INBOX.md", ct)`

#### Scenario: Task output delivered to channel
- **WHEN** the agent response contains actionable content (no "HEARTBEAT_OK" marker)
- **THEN** the scheduler sends the response to the task's configured channel

#### Scenario: Task output is heartbeat OK
- **WHEN** the agent response contains "HEARTBEAT_OK"
- **THEN** the scheduler logs debug and does not send to channel

### Requirement: YAML frontmatter parsed without external library
The system SHALL parse cron task frontmatter using a regex-based parser that extracts key-value pairs from between `---` delimiters. Only the 4 known keys (`schedule`, `agent`, `channel`, `enabled`) are parsed.

#### Scenario: Well-formed frontmatter
- **WHEN** a file starts with `---\nschedule: "0 9 * * *"\nagent: default\nchannel: telegram\nenabled: true\n---`
- **THEN** the parser returns `{ schedule: "0 9 * * *", agent: "default", channel: "telegram", enabled: "true" }`

#### Scenario: Missing frontmatter
- **WHEN** a file has no `---` delimiters
- **THEN** the parser treats the entire file as the prompt body with default metadata (schedule: hourly, agent: default)
