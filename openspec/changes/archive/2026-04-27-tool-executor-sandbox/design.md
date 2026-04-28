## Context

`IToolExecutor` and `ToolCall`/`ToolResult` records are already defined in `Agent/IToolExecutor.cs`. `DisabledToolExecutor` satisfies DI but returns errors for every call. The `appsettings.json` `sandbox` section already declares `type: "bwrap"`, `timeout_ms`, `max_memory_mb`, `network_enabled`, and `allowed_paths`. All tool execution must go through this interface — `AetherSoul` never calls tools directly.

## Goals / Non-Goals

**Goals:**
- Replace `DisabledToolExecutor` with a real executor that runs under `bwrap` on Linux
- Implement six built-in tools: `bash`, `read`, `glob`, `grep`, `write`, `edit`
- Apply timeout and memory limits from config
- Provide a safe `Process`-based fallback for non-Linux (dev machines)

**Non-Goals:**
- Plugin/user-defined tools (future work)
- Network sandboxing beyond disabling by default
- Windows-native sandboxing (restricted Process only)

## Decisions

**D1 — bwrap invocation**: `ToolExecutor` spawns `bwrap` as a child process, passing the actual command via `--`, with `--ro-bind /usr /usr`, `--bind /workspace/group /workspace/group`, etc. derived from `allowed_paths`. Output captured via stdout/stderr pipes.

**D2 — Built-in tools as delegates**: Each built-in tool (`read`, `glob`, `grep`, `write`, `edit`) is implemented as a C# method in `ToolExecutor` that operates directly (no subprocess), because they are safe managed operations with no need for OS-level isolation. Only `bash` goes through `bwrap`/Process.

**D3 — `write` and `edit` are path-restricted**: Both tools validate the target path against `allowed_paths` before any I/O. Paths outside the allowlist return a `ToolResult(false, ..., "Path not permitted")`.

**D4 — Timeout via `CancellationToken`**: The `timeout_ms` from config is applied via `CancellationTokenSource.CancelAfter`. If the token fires, the subprocess is killed and a timeout error is returned.

**D5 — Fallback detection**: Executor checks `OperatingSystem.IsLinux()` and whether `bwrap` binary is on PATH. If either is false, falls back to restricted `Process` with no `bwrap` wrapping but with the same timeout.

## Risks / Trade-offs

- **bwrap not installed**: Fallback is less secure — document that production Linux hosts must have bwrap. Config `type` field can force one mode.
- **Max output truncation**: Large `bash` outputs must be truncated at a configured limit (e.g. 64 KB) to avoid context bloat. Truncation is noted in the returned `Output`.
- **edit tool atomicity**: `edit` performs in-place string replacement. No backup is made — concurrent edits could corrupt files. Acceptable for a single-agent scenario.
