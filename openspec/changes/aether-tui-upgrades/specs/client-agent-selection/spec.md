## ADDED Requirements

### Requirement: TUI startup dynamic agent selection
Both C# and Rust TUI clients SHALL accept a `--agent <name>` command line argument on startup. When provided, the TUI SHALL load the agent profile matching `<name>` (case-insensitive) from `config.json` instead of defaulting to the first enabled agent or hardcoded name.

#### Scenario: Launch C# TUI with specific agent
- **WHEN** the user runs `aether-tui --agent aura`
- **THEN** the C# TUI starts up and connects to the Aura agent
- **AND** the header shows "Aether · aura"
- **AND** the workspace path is resolved to Aura's home directory (e.g. `/home/thoor/agora/familia/aura`)

#### Scenario: Launch Rust TUI with specific agent
- **WHEN** the user runs `clients/aether-tui/tui.sh --agent aura`
- **THEN** the Rust TUI starts up and queries the WebSocket backend specifically for the Aura agent's history and configuration
- **AND** the UI elements show Aura's name and styling

#### Scenario: Launch with invalid/missing agent name
- **WHEN** the user runs `aether-tui --agent invalidagent`
- **THEN** the client print an error message "Agent 'invalidagent' is not configured or enabled" and exit with code 1

### Requirement: TUI release compilation and global installation
The system SHALL support compilation in release mode and install the resulting binary globally to `~/.local/bin/aether-tui` (symlinked or copied). The installed client MUST run successfully from any current working directory.

#### Scenario: Build and install binary
- **WHEN** the user runs the installation script `./aether-update.sh --install`
- **THEN** the C# and Rust clients are compiled in release mode
- **AND** a symlink is created at `~/.local/bin/aether-tui` pointing to the release executable
- **AND** the executable can be run from any folder without relative path dependency errors
