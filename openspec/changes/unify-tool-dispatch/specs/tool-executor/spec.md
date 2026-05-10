## ADDED Requirements

### Requirement: Unified registry dispatch

AetherSoul SHALL execute tool calls through the canonical `Aether.Tooling.ToolExecutor` registry dispatcher.

#### Scenario: Registered tool executes
- **WHEN** the model calls a tool registered in `ToolRegistry`
- **THEN** AetherSoul SHALL dispatch the call through `Aether.Tooling.ToolExecutor`
- **AND** append the normalized result as a tool-result message

#### Scenario: Unknown tool fails clearly
- **WHEN** the model calls a tool that is not registered or not enabled
- **THEN** AetherSoul SHALL append a tool-result failure containing `Tool '<name>' not found` or `not permitted`

#### Scenario: Invalid arguments fail with model-readable error
- **WHEN** the model calls a registered tool with missing required arguments or malformed JSON
- **THEN** `Aether.Tooling.ToolExecutor` SHALL return a failed `ToolResult` with a concise, model-readable error message naming the problem field or validation failure
- **AND** the error SHALL NOT expose stack traces or internal exception details

### Requirement: Compatibility aliases share implementation

The `shell` compatibility alias SHALL execute the same implementation as `bash`. The `exec` compatibility alias SHALL be policy-gated and SHALL execute the same implementation as `bash` only when enabled.

#### Scenario: shell alias works
- **WHEN** the model calls `shell` with a command
- **THEN** the command SHALL be executed by the `bash` implementation under the same sandbox context

#### Scenario: shell produces identical output to bash
- **WHEN** `shell` and `bash` are called with the same command and sandbox context
- **THEN** both SHALL produce identical output for the same command

#### Scenario: exec alias disabled by default
- **WHEN** the model calls `exec` and policy does not permit it
- **THEN** the call SHALL be rejected without executing a command
