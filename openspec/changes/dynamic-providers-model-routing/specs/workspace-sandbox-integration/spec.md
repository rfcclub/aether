## ADDED Requirements

### Requirement: Agent workspace is default sandbox allowed path
The system SHALL allow the agent's workspace directory (from `AgentSpecConfig.Storage.Home`) as a default allowed path for all file operations (`read`, `glob`, `grep`, `write`, `edit`, `bash`) when no explicit sandbox `allowed_paths` are configured for that agent.

#### Scenario: Agent reads file in own workspace
- **WHEN** agent workspace is `/home/thoor/.aether/workspaces/default` and tool call is `read /home/thoor/.aether/workspaces/default/SOUL.md`
- **THEN** `IsPathAllowed()` returns true and the file is read

#### Scenario: Agent writes file in own workspace subdirectory
- **WHEN** agent workspace is `/home/thoor/.aether/workspaces/default` and tool call is `write memory/2026-05-03.md`
- **THEN** the path resolves under the workspace and `IsPathAllowed()` returns true

#### Scenario: Agent tries to access file outside workspace
- **WHEN** agent workspace is `/home/thoor/.aether/workspaces/default` and tool call is `read /etc/passwd`
- **THEN** `IsPathAllowed()` returns false and the tool returns "Path not permitted"

### Requirement: Sandbox allowed_paths from config are additive
When `SandboxOptions.AllowedPaths` are configured in `appsettings.json` or `.aether.json`, those paths SHALL be additional allowed paths beyond the agent workspace.

#### Scenario: Config with extra allowed paths
- **WHEN** appsettings.json specifies `"allowed_paths": ["/workspace/global"]` and agent workspace is `/home/thoor/.aether/workspaces/default`
- **THEN** both `/workspace/global` and the agent workspace are allowed

### Requirement: Per-agent allowed paths from .aether.json
The system SHALL read per-agent allowed paths from `{workspace}/.aether.json` under `"tools": { "file": { "allowedPaths": ["/extra/path"] } }` and add them to the sandbox allowlist for that agent.

#### Scenario: Agent has extra allowed paths in config
- **WHEN** `.aether.json` contains `"tools": { "file": { "allowedPaths": ["/home/thoor/projects"] } }`
- **THEN** the agent can access files in `/home/thoor/projects` in addition to its own workspace

### Requirement: ToolExecutor receives workspace path and per-agent config
The `ToolExecutor` constructor SHALL accept an optional agent workspace path parameter and per-agent `SpecToolsSection` config. When provided, the workspace is automatically added to `_allowedPaths` and per-agent file tool rules are applied.

#### Scenario: ToolExecutor initialized with workspace
- **WHEN** `ToolExecutor` is constructed with workspace `/home/thoor/.aether/workspaces/default` and no explicit sandbox paths
- **THEN** `_allowedPaths` contains `["/home/thoor/.aether/workspaces/default"]`

#### Scenario: ToolExecutor initialized without workspace (backward compatibility)
- **WHEN** `ToolExecutor` is constructed without a workspace (old constructor signature)
- **THEN** `_allowedPaths` contains only paths from `SandboxOptions.AllowedPaths` as before

### Requirement: Sandbox type none disables all path restrictions
When the sandbox type is configured as `"none"`, the system SHALL skip all path validation and allow all file operations at any path.

#### Scenario: Sandbox type is none
- **WHEN** `SandboxOptions.Type` is `"none"` or `.aether.json` `policy.sandboxBackend` is `"none"`
- **THEN** `IsPathAllowed()` returns true for any path, including `/etc/passwd`

#### Scenario: Sandbox type is bwrap or process (normal enforcement)
- **WHEN** `SandboxOptions.Type` is `"bwrap"` or `"process"`
- **THEN** `IsPathAllowed()` enforces the configured allowed paths as normal

### Requirement: Per-agent denied paths restrict even workspace access
The system SHALL read per-agent denied paths from `.aether.json` under `"tools": { "file": { "deniedPaths": [...] } }`. Paths matching denied entries SHALL be blocked even if they are within the workspace.

#### Scenario: Agent denied access to _INTEGRITY directory
- **WHEN** `.aether.json` contains `"tools": { "file": { "deniedPaths": ["_INTEGRITY/"] } }`
- **THEN** any file operation targeting `{workspace}/_INTEGRITY/` returns "Path not permitted"
