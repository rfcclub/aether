## ADDED Requirements

### Requirement: Daily review fires at midnight UTC
The system SHALL run a daily review as an `IHostedService` that triggers at midnight UTC each day, inspecting session history for friction signals.

#### Scenario: Cron fires at midnight UTC
- **WHEN** the system clock reaches midnight UTC on any day
- **THEN** the daily review SHALL begin within 60 seconds of midnight

#### Scenario: Cron does not fire outside midnight window
- **WHEN** the system clock is more than 1 minute past midnight UTC
- **THEN** the cron SHALL wait until the next midnight UTC window

#### Scenario: Cron survives transient exceptions
- **WHEN** the daily review logic throws an unhandled exception
- **THEN** the cron SHALL log the error and continue running, firing at the next midnight window

### Requirement: Daily review scans session history for friction signals
The daily review SHALL query the working memory layer for sessions from the prior 24 hours, scanning for correction patterns, assistant refusals, and repeated tool failures.

#### Scenario: Sessions contain correction patterns
- **WHEN** sessions from the prior day contain messages where a user corrected the assistant or a tool call returned an error
- **THEN** the review SHALL identify each friction point and include it in the daily reflections file

#### Scenario: No sessions exist for prior day
- **WHEN** no sessions have activity in the prior 24 hours
- **THEN** the review SHALL write an empty reflections file with a note indicating no activity and log an informational message

### Requirement: Daily review writes reflections to patches directory
The daily review SHALL write its findings to `patches/reflections-<YYYY-MM-DD>.md` with one section per friction point, including the session ID, timestamp, and extracted relevant message content.

#### Scenario: Reflections file written successfully
- **WHEN** the daily review completes with 2 friction points identified
- **THEN** a file SHALL exist at `patches/reflections-<date>.md` with 2 friction point sections, each containing session ID, timestamp, and message excerpt

#### Scenario: Patches directory does not exist
- **WHEN** the `patches/` directory does not exist
- **THEN** the daily review SHALL create it before writing the reflections file
