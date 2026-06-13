# Intent: aether-tui-upgrades

## Raw Request

🖥️ Phía Client TUI (Rust / C#)
• Hỗ trợ flag chọn agent khi khởi động (ví dụ: aether-tui --agent aura) để đổi người trò chuyện ngay lập tức.
• Biên dịch release để chạy aether-tui từ bất kỳ workspace nào trên terminal.

## Problem

The C# and Rust TUI clients currently default to a single agent (like `maria`) and do not support dynamic agent switching via CLI arguments. Furthermore, the clients are run using local debug build tools (like `dotnet run` or local scripts), which makes them difficult to run globally from any arbitrary workspace directory.

## Desired Outcome

- Support the `--agent <name>` flag at startup for both C# and Rust TUI clients, allowing the user to select their chat partner dynamically.
- Build the client binaries in release mode (`cargo build --release` for Rust, `dotnet publish` for C#) and configure them to be executable from any terminal workspace folder using standard system paths (e.g., symlinked to `~/.local/bin/aether-tui` or `aether-tui`).

## Users / Actors

- Thoor (using the terminal interface).

## Current Context

Currently, running the C# TUI uses `./aether-tui.sh` and defaults to `"maria"`. Switching agents requires changing DI registrations or config file values. Running the Rust TUI requires launching `./clients/aether-tui/tui.sh --build`.

## Proposed Direction

- Update the startup argument parser in both TUI projects.
- Map the parsed `--agent` argument to the agent configuration section, loading the appropriate workspace and system prompts.
- Write packaging scripts to build and copy the release binaries to global execution folders.

## Scope

- Client TUI applications (`src/Aether.Tui/` and `clients/aether-tui/`).
- Launch and installation scripts.

## Non-Goals

- Dynamically switching agents *during* an active UI session without restarting the client.
- Creating a graphical installer.

## Constraints

- Changing the agent via CLI parameter must not break database history sync or agent-specific memory.
- Must run cleanly on macOS.

## Success Criteria

- Running `aether-tui --agent aura` correctly launches the TUI client connected to the Aura agent with her workspace, soul, and memory loaded.
- The compiled client can be executed as `aether-tui` from any folder outside the repository.

## Risks

- File paths resolved relatively in the agent workspace could break when executed from a different current working directory (CWD).
- Mitigation: Always resolve agent workspace paths absolutely based on `config.json` configuration rather than using relative paths to CWD.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- None.

## Assumptions

- `aether-tui` binary will be placed in a directory included in the user's `$PATH` (like `~/.local/bin/` or `/usr/local/bin`).

## Spec Seeds

- Startup CLI parameter `--agent <name>`.
- Absolute path resolution logic.

## Intent Approval

Status: APPROVED
Approved by: Thoor
Date: 2026-06-13
