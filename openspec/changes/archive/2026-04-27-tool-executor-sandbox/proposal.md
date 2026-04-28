## Why

`AetherSoul` currently uses `DisabledToolExecutor`, meaning all tool calls silently fail. Without a working tool executor, Aether cannot execute bash commands, read files, or interact with the filesystem — making the agent loop functionally useless beyond LLM conversation.

## What Changes

- Replace `Agent/DisabledToolExecutor.cs` with a real `Agent/ToolExecutor.cs`
- Implement `bwrap` (bubblewrap) sandbox on Linux with a restricted `Process` fallback for Windows/macOS
- Add built-in tools: `bash`, `read`, `glob`, `grep`, `write`, `edit`
- Apply per-tool timeouts and resource limits (CPU time, max output size)
- Register the real executor in DI, replacing the disabled stub

## Capabilities

### New Capabilities

- `tool-executor`: Sandboxed tool execution with bwrap/Process backend and built-in tool implementations

### Modified Capabilities

- (none)

## Impact

- **Replaces**: `Agent/DisabledToolExecutor.cs` (deleted or kept as fallback)
- **New files**: `Agent/ToolExecutor.cs`, `Agent/Tools/` (one file per built-in tool)
- **Dependency**: `bwrap` system binary on Linux; no new NuGet packages required
- **Config**: timeout and resource limits from `appsettings.json` `sandbox` section
