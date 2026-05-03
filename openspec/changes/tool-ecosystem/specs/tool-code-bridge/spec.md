## ADDED Requirements

### Requirement: Code-registered tool implementations

The system SHALL support an `IToolImplementation` interface that allows tools to provide real execution logic from C# code, replacing the passive stub behavior of hot-reloaded tools.

#### Scenario: Tool references implementation by name
- **WHEN** a hot-reloaded tool JSON contains `"implementation": "web_search"` and an `IToolImplementation` with `Name = "web_search"` is registered in DI
- **THEN** the tool SHALL delegate execution to the registered implementation

#### Scenario: Tool without implementation falls back to stub
- **WHEN** a hot-reloaded tool JSON does NOT contain `"implementation"` field
- **THEN** the tool SHALL use the current passive stub behavior (log + return mock result)

#### Scenario: Tool references unknown implementation
- **WHEN** a hot-reloaded tool JSON contains `"implementation": "nonexistent"` but no such implementation is registered
- **THEN** system SHALL log a warning and fall back to passive stub behavior

### Requirement: IToolImplementation interface

The system SHALL define `IToolImplementation` with `Name`, `ParametersSchema`, and `ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct)`.

#### Scenario: Implementation provides parameter schema
- **WHEN** `IToolImplementation.ParametersSchema` is accessed
- **THEN** it SHALL return a valid JSON Schema for the tool's accepted parameters

### Requirement: ISandboxContext injection

All tool implementations SHALL receive an `ISandboxContext` that provides workspace path and path validation.

#### Scenario: Tool checks path before file operation
- **WHEN** a file tool implementation receives `ISandboxContext`
- **THEN** it SHALL call `sandbox.IsPathAllowed(path)` before reading or writing
