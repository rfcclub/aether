## Why

Providing a dynamic runtime compilation capability for custom C# tools allows developers or the agent itself to add new tool features instantly (hot-reload code) without restarting the Aether hosting process or recompiling the entire solution.

## What Changes

- Implement `DynamicToolCompilerService` using the Roslyn API (`Microsoft.CodeAnalysis.CSharp`).
- Watch `.cs` files inside the `tools/` folder.
- Dynamic interface `IDynamicTool` to standardise execution structure.
- Auto-inject and register compiled tools into `ToolRegistry`.

## Capabilities

### New Capabilities
- `dynamic-csharp-compilation`: Compiles raw C# files on-the-fly and loads them into the current running process safely.
- `dynamic-tool-registration`: Registers dynamic classes implementing `IDynamicTool` directly into `ToolRegistry` and `ToolExecutor` bindings.

### Modified Capabilities
- `tool-hot-reload`: Expand hot-reload checks to watch both JSON files (schemas) and `.cs` files (implementations).

## Impact

- `src/Aether/Tooling/ToolHotReloadService.cs` (extended)
- `src/Aether/Tooling/DynamicToolCompilerService.cs` (new service)
- `src/Aether/Tooling/IDynamicTool.cs` (new interface)

