## Why

Tool definitions are hard-coded into `ToolRegistry` at startup. Adding or modifying tools requires application restart. The spec already requires FileSystemWatcher-based hot-reload for `tools/*.json` files — this change implements it.

## What Changes

- Add `ToolHotReloadService` — `IHostedService` that watches `tools/` directory via `FileSystemWatcher`
- Parse `.json` tool definition files on create/modify, register/unregister in `IToolRegistry`
- Add `UnregisterTool` to `IToolRegistry` interface
- 2-second debounce to handle partial writes
- Wire into DI in Program.cs
- Support configurable `tools/` path via `appsettings.json`

## Capabilities

### Modified Capabilities
- `tool-system`: Implement the existing "Hot-reload via FileSystemWatcher" requirement. Update Purpose.

## Impact

- `src/Aether/Tooling/IToolRegistry.cs` — add `UnregisterTool`
- `src/Aether/Tooling/ToolRegistry.cs` — implement unregister, make reload-aware
- `src/Aether/Tooling/ToolHotReloadService.cs` — new: BackgroundService + FileSystemWatcher
- `src/Aether/Program.cs` — register new service
