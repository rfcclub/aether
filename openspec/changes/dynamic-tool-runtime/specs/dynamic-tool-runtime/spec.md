## ADDED Requirements

### Requirement: dynamic-compilation
The compiler service SHALL watch the `tools/` directory for files ending in `.cs` and compile them into memory using Roslyn compiler APIs.

#### Scenario: Successful compilation of C# tool
- **WHEN** A file `tools/FetchJoke.cs` is added containing a class `FetchJoke` implementing `IDynamicTool`
- **THEN** The assembly compiles in memory without error and the tool is added to the active registry

### Requirement: compile-error-resilience
If dynamic compilation fails, the compiler service SHALL write compile errors to logs and preserve all existing tools.

#### Scenario: Compiling broken C# tool
- **WHEN** A file `tools/BrokenTool.cs` is added with syntax errors
- **THEN** The compiler fails to load it, logs the errors, and the existing tools continue to execute normally

