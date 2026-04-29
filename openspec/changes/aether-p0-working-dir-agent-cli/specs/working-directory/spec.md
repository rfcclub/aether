# working-directory Specification

## Purpose

Define the `~/.aether/` runtime directory — its auto-creation, idempotency, directory tree contract, and the device identity file. This is the first subsystem initialized on any Aether execution.

## ADDED Requirements

### Requirement: Working directory auto-created on startup

Aether SHALL check for the existence of `~/.aether/` (or `$AETHER_HOME` if set) on every startup. If the directory does not exist, Aether SHALL create it with the full directory tree before any other subsystem initializes.

#### Scenario: First run creates directory tree

- **WHEN** `~/.aether/` does not exist and Aether starts
- **THEN** the system SHALL create `~/.aether/` and all subdirectories: `identity/`, `agents/`, `workspaces/`, `store/`, `cron/`, `logs/`, `backups/`

#### Scenario: Subsequent runs are idempotent

- **WHEN** `~/.aether/` already exists and Aether starts
- **THEN** the system SHALL NOT overwrite or modify any existing files or directories

#### Scenario: Custom AETHER_HOME respected

- **WHEN** `AETHER_HOME` environment variable is set to `/custom/path`
- **THEN** Aether SHALL use `/custom/path` as the working directory instead of `~/.aether/`

### Requirement: Device identity file created once

On first run, Aether SHALL create `~/.aether/identity/device.json` containing a unique device ID (UUID v4), the creation timestamp in ISO 8601, and the Aether version that created it.

#### Scenario: Device identity generated on first run

- **WHEN** `~/.aether/` is created for the first time
- **THEN** `identity/device.json` SHALL exist with keys `deviceId` (UUID), `createdAt` (ISO 8601), and `version` (semver string)

#### Scenario: Device identity preserved across runs

- **WHEN** `~/.aether/` already exists with a valid `identity/device.json`
- **THEN** the file SHALL NOT be regenerated or modified

### Requirement: Directory tree matches contract

The created directory tree SHALL match the defined contract. No extra directories SHALL be created beyond the contract. Missing optional agent subdirectories (`plans/`, `research/`) SHALL be created per-agent by the agent scaffolding command, not by working directory init.

#### Scenario: Directory tree structure verified

- **WHEN** working directory initialization completes
- **THEN** the following directories SHALL exist: `.`, `identity/`, `agents/`, `workspaces/`, `store/`, `cron/`, `logs/`, `backups/`

#### Scenario: Agent-specific directories not pre-created

- **WHEN** working directory initialization completes
- **THEN** `agents/` and `workspaces/` SHALL be empty (no agent subdirectories pre-created)

### Requirement: Initialization runs before database and provider

The `WorkingDirectoryInitializer` SHALL execute before `AetherDb.InitializeAsync()` and any provider connection in the startup sequence.

#### Scenario: Working directory exists before database init

- **WHEN** Aether host starts
- **THEN** `WorkingDirectoryInitializer.InitializeAsync()` SHALL complete before `AetherDb.InitializeAsync()` is called

#### Scenario: Working directory failure blocks startup

- **WHEN** `WorkingDirectoryInitializer.InitializeAsync()` throws (e.g., permission denied on `~/.aether/`)
- **THEN** the host SHALL log the error and stop — no database initialization or provider connection SHALL occur
