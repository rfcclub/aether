# tui-config-loader — Specification

> Capability: Config resolution chain for aether-tui connecting to the correct Aether backend.

---

## ADDED Requirements

### Requirement: Config resolution chain executes in priority order

The system SHALL resolve the WebSocket URL using the following chain, stopping at the first successful resolution:

1. `~/.aether/config.json` → read `agents.maria.workspace` → read `{workspace}/.aether.json` → extract port
2. Environment variable `AETHER_WS_URL` (full WebSocket URL, e.g. `ws://host:port/ws`)
3. Fallback: `ws://localhost:5099/ws`

#### Scenario: Config files present with correct port

- **WHEN** `~/.aether/config.json` exists with `agents.maria.workspace` set to `/path/to/workspace`
- **AND** `/path/to/workspace/.aether.json` exists with `websocket.port` or `port` set to `5099`
- **THEN** the resolved URL SHALL be `ws://localhost:5099/ws`

#### Scenario: Env var override

- **WHEN** `AETHER_WS_URL=ws://remote-host:6000/ws` is set in the environment
- **THEN** the resolved URL SHALL be `ws://remote-host:6000/ws` regardless of config files

#### Scenario: No config files, no env var

- **WHEN** neither `~/.aether/config.json` nor `AETHER_WS_URL` exists
- **THEN** the resolved URL SHALL be `ws://localhost:5099/ws`
- **THEN** the status bar SHALL display `[default]` hint next to the connection URL

---

### Requirement: Config parse errors are non-fatal

The system SHALL continue to the next resolution step if any config file is malformed or missing, rather than crashing.

#### Scenario: Malformed config.json

- **WHEN** `~/.aether/config.json` contains invalid JSON
- **THEN** the system SHALL log a warning message to the status bar (`Config parse error, using fallback`)
- **THEN** the system SHALL proceed to the env var check
- **THEN** NO panic or process exit SHALL occur

#### Scenario: Workspace .aether.json missing

- **WHEN** `~/.aether/config.json` resolves a workspace path but `.aether.json` does not exist in that workspace
- **THEN** the system SHALL fall through to the env var / fallback step

---

### Requirement: CLI flag overrides all config

The system SHALL accept a `--url` CLI argument that overrides the entire config chain.

#### Scenario: CLI URL argument provided

- **WHEN** user runs `aether-tui --url ws://192.168.1.10:5099/ws`
- **THEN** the resolved URL SHALL be `ws://192.168.1.10:5099/ws`
- **THEN** no config files SHALL be read
