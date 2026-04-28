## 1. Interface Update

- [x] 1.1 Add `UnregisterTool(string name)` to `IToolRegistry` interface
- [x] 1.2 Implement `UnregisterTool` in `ToolRegistry`

## 2. Hot Reload Service

- [x] 2.1 Create `ToolHotReloadService.cs` as `BackgroundService`
- [x] 2.2 Initialize `FileSystemWatcher` on `tools/` directory, filter `*.json`
- [x] 2.3 Implement 2-second debounce — timer reset on each event, processing after silence
- [x] 2.4 Parse tool definition JSON on create/modify, call `IToolRegistry.Register`
- [x] 2.5 Handle delete events — call `IToolRegistry.UnregisterTool`
- [x] 2.6 Handle invalid JSON — log error, skip file, don't crash
- [x] 2.7 Create `tools/` directory if missing on startup
- [x] 2.8 Make tools path configurable via constructor (`string toolsPath`)

## 3. DI Wiring

- [x] 3.1 Register `ToolHotReloadService` as `IHostedService` in Program.cs
- [x] 3.2 Pass configured `tools/` path from `IConfiguration` (default: `tools`)
- [x] 3.3 Create `tools/.gitkeep` file

## 4. Tests

- [x] 4.1 Test: Creating a `.json` file in tools/ triggers registration within debounce window
- [x] 4.2 Test: Deleting a `.json` file triggers unregistration
- [x] 4.3 Test: Invalid JSON file is skipped, no crash
- [x] 4.4 Test: Debounce prevents duplicate registration from rapid create+change events
- [x] 4.5 Test: `UnregisterTool` removes tool from registry
