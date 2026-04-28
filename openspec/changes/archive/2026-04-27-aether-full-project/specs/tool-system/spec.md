## ADDED Requirements

### Requirement: JSON Schema validation before tool dispatch
`IToolExecutor.ExecuteAsync` SHALL validate the `args` JSON element against the tool's `ParametersJson` schema using NJsonSchema before invoking the tool delegate.

#### Scenario: Valid arguments pass through
- **WHEN** a tool call's arguments match the registered JSON Schema
- **THEN** the executor SHALL invoke the tool delegate with the validated arguments

#### Scenario: Invalid arguments rejected
- **WHEN** a tool call's arguments violate the JSON Schema (missing required field, wrong type)
- **THEN** the executor SHALL return `ToolResult.Failure("schema validation failed: <details>")` without invoking the delegate

### Requirement: Permission model enforces per-session allowlists
Each `AetherSoul` session SHALL have an associated tool allowlist. `ExecuteAsync` SHALL reject calls to tools not on the session's allowlist.

#### Scenario: Tool on allowlist executes
- **WHEN** the session allowlist contains `"read"` and the agent calls `read`
- **THEN** the executor SHALL proceed normally

#### Scenario: Tool not on allowlist rejected
- **WHEN** the session allowlist does not contain `"exec"` and the agent calls `exec`
- **THEN** the executor SHALL return `ToolResult.Failure("tool 'exec' not permitted in this session")`

### Requirement: Hot-reload via FileSystemWatcher
The tool registry SHALL monitor a configurable `tools/` directory and reload tool definitions when `.json` definition files are created, modified, or deleted.

#### Scenario: New tool file detected
- **WHEN** a `tools/mytool.json` file is created while the host is running
- **THEN** the registry SHALL register the new tool within 2 seconds without restarting the host

#### Scenario: Tool file deleted
- **WHEN** a `tools/mytool.json` file is deleted
- **THEN** the registry SHALL unregister the tool; subsequent calls SHALL return `ToolResult.Failure("tool not found")`

### Requirement: Built-in tools registered at startup
The following tools SHALL be registered by default: `read`, `write`, `edit`, `bash`, `glob`, `grep`.

#### Scenario: Built-in tool available immediately
- **WHEN** the host starts
- **THEN** all six built-in tools SHALL be resolvable from `IToolRegistry` without any configuration
