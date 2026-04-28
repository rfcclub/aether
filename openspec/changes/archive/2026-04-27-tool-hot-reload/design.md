## Context

`ToolRegistry` currently loads built-in tools at construction. No mechanism exists to add/remove tools at runtime. The `tool-system` spec requires FileSystemWatcher on `tools/` directory for `.json` definition files.

## Goals / Non-Goals

**Goals:**
- Watch `tools/` directory for `.json` files via `FileSystemWatcher`
- Parse tool definitions from JSON, register on create/modify
- Unregister on delete
- 2-second debounce to handle partial writes and avoid duplicate registrations
- Configurable path, default `tools/`

**Non-Goals:**
- Schema validation of tool definition JSON (rely on existing NJsonSchema pipeline)
- Plugin system — hot-reload is file-based only
- Hot-reload for skills (separate concern)

## Decisions

### 1: FileSystemWatcher in BackgroundService vs inside ToolRegistry

**Decision:** Separate `ToolHotReloadService` as `BackgroundService`.

**Rationale:** Keeps `ToolRegistry` focused on registration/lookup. The watcher is infrastructure — separate class, testable independently, clean lifecycle via `IHostedService`.

### 2: Debounce via Timer vs file hash comparison

**Decision:** 2-second `System.Threading.Timer` debounce per path.

**Rationale:** FileSystemWatcher fires multiple events per save (Created + Changed). A timer that resets on each event and fires after 2s of silence handles this without content hashing overhead.

### 3: Tool definition JSON format

**Decision:** Same format as `ParametersJson` / `SchemaJson` structure already on `LlmTool`:
```json
{
  "name": "my-tool",
  "description": "does something",
  "parameters_json": "{...}",
  "schema_json": "{...}"
}
```

**Rationale:** Consistent with existing tool model. No new format to learn.

## Risks / Trade-offs

- **Partial writes**: File may be read mid-write → Mitigation: 2s debounce, atomic write by user
- **Invalid JSON**: Parse failure logged, file skipped, existing tools unaffected
- **Directory missing**: `tools/` may not exist → Mitigation: Create directory on startup if missing
