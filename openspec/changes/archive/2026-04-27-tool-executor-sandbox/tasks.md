## 1. Core ToolExecutor Shell

- [x] 1.1 Create `Agent/ToolExecutor.cs` implementing `IToolExecutor`, reading sandbox config from `IConfiguration`
- [x] 1.2 Add `SandboxOptions` record bound to `appsettings.json` `sandbox` section
- [x] 1.3 Register `ToolExecutor` in `Program.cs` DI, replacing `DisabledToolExecutor`

## 2. Bash Tool (bwrap / Process)

- [ ] 2.1 Implement `ExecuteBashAsync`: detect Linux + bwrap availability
- [ ] 2.2 Build `bwrap` argument list from `allowed_paths` and `network_enabled`
- [ ] 2.3 Implement restricted `Process` fallback for non-Linux / no-bwrap
- [ ] 2.4 Apply `timeout_ms` via `CancellationTokenSource.CancelAfter`, kill process on timeout
- [ ] 2.5 Capture stdout+stderr, truncate at 64 KB with truncation suffix

## 3. Safe Built-in Tools

- [x] 3.1 Implement `read` tool: read file within allowed paths, return contents
- [x] 3.2 Implement `glob` tool: `Directory.GetFiles` with pattern matching, return newline-separated paths
- [x] 3.3 Implement `grep` tool: line-by-line regex search, optional context lines
- [ ] 3.4 Implement `write` tool: write file after path allowlist check
- [ ] 3.5 Implement `edit` tool: replace first occurrence of `old_string` with `new_string` after path check

## 4. Path Validation

- [x] 4.1 Add `IsPathAllowed(string path)` helper checking against `allowed_paths` (resolves symlinks via `Path.GetFullPath`)
- [ ] 4.2 Apply `IsPathAllowed` to `read`, `write`, `edit`; return `ToolResult(false, "", "Path not permitted")` on violation

## 5. Dispatch and Unknown Tools

- [x] 5.1 Add dispatch switch in `ExecuteAsync`: route by `call.Name` to the correct handler
- [x] 5.2 Return `ToolResult(false, "", "Unknown tool: <name>")` for unrecognized tool names

## 6. Tests

- [x] 6.1 Add smoke test: `read` returns content for a file inside allowed path
- [x] 6.2 Add smoke test: `read` returns error for path outside allowed paths
- [x] 6.3 Add smoke test: `glob` returns matching files
- [x] 6.4 Add smoke test: `grep` finds pattern in file
- [ ] 6.5 Add smoke test: `write` then `read` round-trip within allowed path
- [ ] 6.6 Add smoke test: `edit` replaces content in a file
- [x] 6.7 Add smoke test: unknown tool returns expected error
- [ ] 6.8 Add smoke test: bash timeout is enforced (sleep command exceeding limit)
- [x] 6.9 Add smoke test: bash/write/edit return explicit not-enabled errors in the safe-tool slice
