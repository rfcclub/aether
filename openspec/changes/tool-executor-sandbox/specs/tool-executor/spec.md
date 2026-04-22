## ADDED Requirements

### Requirement: Sandbox Execution

The system MUST execute `bash` tool calls inside a `bwrap` sandbox on Linux, or a restricted `Process` on non-Linux platforms.

#### Scenario: bash tool runs under bwrap on Linux
- **WHEN** `ExecuteAsync` is called with `Name = "bash"` and `bwrap` is available on Linux
- **THEN** the command is wrapped in `bwrap` with `allowed_paths` bind-mounted read-write, network disabled, and output is captured

#### Scenario: bash tool falls back on non-Linux
- **WHEN** `ExecuteAsync` is called with `Name = "bash"` and the host is not Linux or bwrap is not found
- **THEN** the command runs via `Process.Start` with a working directory restricted to the first allowed path and the same timeout applied

#### Scenario: Timeout is enforced
- **WHEN** a `bash` command exceeds `timeout_ms` milliseconds
- **THEN** the process is killed and `ToolResult(false, "", "Execution timed out")` is returned

### Requirement: Built-in Safe Tools

The executor MUST implement `read`, `glob`, `grep`, `write`, and `edit` as managed C# operations without a subprocess.

#### Scenario: read returns file contents
- **WHEN** `ExecuteAsync` is called with `Name = "read"` and argument `path` points to a file within `allowed_paths`
- **THEN** `ToolResult(true, <file contents>)` is returned

#### Scenario: glob returns matching file paths
- **WHEN** `ExecuteAsync` is called with `Name = "glob"` and arguments `pattern` and `root`
- **THEN** `ToolResult(true, <newline-separated paths>)` is returned for all matches

#### Scenario: grep returns matching lines
- **WHEN** `ExecuteAsync` is called with `Name = "grep"` with arguments `pattern`, `path`, and optional `context_lines`
- **THEN** `ToolResult(true, <matching lines with optional context>)` is returned

#### Scenario: write creates or overwrites a file
- **WHEN** `ExecuteAsync` is called with `Name = "write"` with arguments `path` and `content` and the path is within `allowed_paths`
- **THEN** the file is written and `ToolResult(true, "Written")` is returned

#### Scenario: edit replaces content in a file
- **WHEN** `ExecuteAsync` is called with `Name = "edit"` with arguments `path`, `old_string`, and `new_string` and the path is within `allowed_paths`
- **THEN** the first occurrence of `old_string` is replaced with `new_string` and `ToolResult(true, "Edited")` is returned

### Requirement: Path Restriction

Write tools (`write`, `edit`) MUST refuse operations on paths outside `allowed_paths`.

#### Scenario: write outside allowed paths is rejected
- **WHEN** `ExecuteAsync` is called with `Name = "write"` and a `path` outside `allowed_paths`
- **THEN** `ToolResult(false, "", "Path not permitted")` is returned without touching the filesystem

#### Scenario: read outside allowed paths is rejected
- **WHEN** `ExecuteAsync` is called with `Name = "read"` and a `path` outside `allowed_paths`
- **THEN** `ToolResult(false, "", "Path not permitted")` is returned

### Requirement: Output Truncation

Large `bash` outputs MUST be truncated to prevent context bloat.

#### Scenario: output exceeds limit
- **WHEN** combined stdout+stderr from a bash command exceeds 65536 bytes
- **THEN** the output is truncated and a suffix `\n[Output truncated at 64KB]` is appended

### Requirement: Unknown Tool Handling

#### Scenario: unknown tool name
- **WHEN** `ExecuteAsync` is called with an unrecognized `Name`
- **THEN** `ToolResult(false, "", "Unknown tool: <name>")` is returned
