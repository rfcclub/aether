## Why

Currently, the C# and Rust TUI clients are hardcoded to boot for a single agent and run out of development environments. Providing CLI-based agent selection and a global installation script enables multi-agent operation directly from any workspace directory.

## What Changes

- **Add `--agent` flag**: Both TUI clients SHALL parse the `--agent <name>` command line argument at startup to run the session specifically for the requested agent.
- **Global installation**: Build release packages and symlink them to `~/.local/bin/aether-tui` to run globally.

## Capabilities

### New Capabilities
- `client-agent-selection`: Accept startup flags and dynamically load agent configuration, profiles, and backend WebSockets.

### Modified Capabilities
- *(none)*

## Impact

- `src/Aether.Tui/Program.cs`: Update argument parsing, config path resolution, and dynamic `AgentProfile` DI binding.
- `clients/aether-tui/src/config.rs` and startup scripts: Update argument parsing and history endpoint requests.
- `aether-update.sh`: Add build-and-install options for client packaging.
