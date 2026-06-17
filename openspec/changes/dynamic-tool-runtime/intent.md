# Intent: dynamic-tool-runtime

## Raw Request

"còn gì em có thể làm để có thể hoàn thiện Aether ? Như MCP support, tool support, plugin, A2A protocol, hay những gì tà đạo em nghĩ ra ?" -> "dùng LoomKit tạo intent và specs rồi bỏ trong thư mục openspec, và làm B, C, A, D" (C: Dynamic C# tool runtime)

## Problem

Aether tool addition currently requires project re-compilation, which limits the flexibility of dynamic agents during active coding sessions. Modifying or adding a custom tool behavior requires restarting the Aether host process. We need a way to hot-reload not just tool schemas but the actual C# code implementing them.

## Desired Outcome

A watched directory (`tools/`) allows writing raw C# `.cs` files (e.g. `SearchStackOverflow.cs`). When a change is detected, Aether compiles the class on the fly, dynamically updates `ToolRegistry` and `IToolExecutor` bindings, and makes the tool available immediately without host restarts.

## Users / Actors

- **Developer / Agent:** Writes or patches a C# file under `tools/` and uses the newly compiled tool immediately in the current chat session.

## Current Context

- `ToolHotReloadService.cs` watches for JSON schemas in the `tools/` folder.
- Dynamic assembly load capabilities of .NET Core are mature.

## Proposed Direction

- Expand `ToolHotReloadService.cs` or create a companion service `DynamicToolCompilerService` that watches for `.cs` files.
- Use Roslyn compilation API (`Microsoft.CodeAnalysis.CSharp`) or CSScript to compile the file into an in-memory assembly.
- Extract subclasses of a unified `IDynamicTool` or `BaseTool` interface, instantiate them via reflection, and register their definitions and execution logic into `ToolRegistry` and `ToolExecutor`.

## Scope

- In-memory Roslyn compilation of C# files.
- Integration with `FileSystemWatcher` (2-second debounce).
- Interface/abstract class `IDynamicTool` defining the runtime contract.
- Safe isolation: compilation warnings/errors logged without crashing the Aether host.

## Non-Goals

- Complex dependency resolution for dynamic C# scripts (they should only rely on standard BCL and references exposed by Aether).
- Compiling complex multi-file assemblies.

## Constraints

- Code compilation must be sandboxed or restricted to avoid unsafe native calls.
- Memory leak protection (proper assembly unloading or reusing assemblies where possible).

## Success Criteria

- Placing `HelloWorldTool.cs` in the `tools/` folder registers the `hello_world` tool, which returns "Hello World" when executed.

## Risks

- Memory exhaustion from repeatedly compiling and loading assemblies.
- Mitigation: Limit compile iterations or reload the compilation context.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- Can the agent itself write these `.cs` files to self-evolve its capabilities? -> Yes, this is the main "tà đạo" outcome.

## Assumptions

- Roslyn compiler packages are available as compile-time dependencies of Aether.

## Spec Seeds

- Aether watches `tools/*.cs` files.
- Dynamic tools implement:
  ```csharp
  public interface IDynamicTool {
      string Name { get; }
      string Description { get; }
      string ParameterSchemaJson { get; }
      Task<string> ExecuteAsync(Dictionary<string, object> args);
  }
  ```

## Intent Approval

Status: APPROVED

Approved by: Thoor
Date: 2026-06-16

