## MODIFIED Requirements

### Requirement: Hot-reload via FileSystemWatcher
The tool registry SHALL monitor a configurable `tools/` directory and reload tool definitions when `.json` definition files are created, modified, or deleted. Monitoring SHALL be implemented by `ToolHotReloadService` as an `IHostedService`. A 2-second debounce SHALL prevent duplicate registrations from partial writes.

#### Scenario: New tool file detected
- **WHEN** a `tools/mytool.json` file is created while the host is running
- **THEN** the registry SHALL register the new tool within 2 seconds without restarting the host

#### Scenario: Tool file modified
- **WHEN** a `tools/mytool.json` file is modified
- **THEN** the registry SHALL reload the tool definition within 2 seconds

#### Scenario: Tool file deleted
- **WHEN** a `tools/mytool.json` file is deleted
- **THEN** the registry SHALL unregister the tool; subsequent calls SHALL return `ToolResult.Failure("tool not found")`

#### Scenario: Invalid JSON skipped
- **WHEN** a `tools/bad.json` file contains invalid JSON
- **THEN** the system SHALL log the parse error and continue watching; all existing tools SHALL remain registered

#### Scenario: Tools directory missing on startup
- **WHEN** the configured `tools/` directory does not exist at startup
- **THEN** the system SHALL create it before starting the watcher
