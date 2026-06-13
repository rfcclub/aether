## Architecture

- **CLI Argument Parsing**: We will use basic command-line argument parsing in `Program.cs` (C#) and `main.rs`/`args.rs` (Rust) to scan for `--agent` (or `-a`).
- **Dynamic Configuration Injection**: Instead of hardcoding `"maria"`, the TUI startup code will use the parsed agent name to query the agent registry in the global `config.json` via `ConfigLoader`, loading the appropriate workspace.
- **Global Path Mapping**: The global installation script will build the binaries in release mode and install a symlink to `~/.local/bin/aether-tui` pointing to the release package, ensuring CWD-independent path resolving by reading from `AETHER_HOME` config paths.

## Components

- **C# TUI App (`src/Aether.Tui/Program.cs`)**:
  - Scans `args` for `--agent` or `-a`.
  - Passes the parsed name to the `AgentProfile` registration.
- **Rust TUI App (`clients/aether-tui/src/main.rs`)**:
  - Parses `--agent <name>`.
  - Connects to the backend websocket on port 5099 with connection headers/handshake specifying the agent group.
- **Build/Install Script (`aether-update.sh`)**:
  - Compiles the release binaries (`dotnet publish -c Release` and `cargo build --release`).
  - Creates a symlink in `~/.local/bin/`.

## Data Model

No database schema changes. The existing agent models and configuration layouts support dynamic name mapping.

## Test Strategy

| Scenario ID | Test File | Type |
|-------------|-----------|------|
| `Launch C# TUI with specific agent` | `tests/Aether.Tests/TuiStartupTests.cs` | integration |
| `Launch with invalid/missing agent name` | `tests/Aether.Tests/TuiStartupTests.cs` | unit |

## Dependencies

- None.

## Migration

This change is non-disruptive but introduces `--agent` as a new startup parameter. Old launch commands without arguments remain fully functional and default to the primary enabled agent.
