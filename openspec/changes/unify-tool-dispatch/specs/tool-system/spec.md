## ADDED Requirements

### Requirement: Registry-backed LLM tool exposure

AetherSoul SHALL expose enabled tools from `ToolRegistry` to the LLM instead of relying on a hardcoded tool list.

#### Scenario: Registry tools appear in provider request
- **WHEN** `ToolRegistry` contains `read`, `bash`, `web_search`, and `web_fetch`
- **AND** AetherSoul sends an LLM request
- **THEN** the request tool definitions SHALL include all four tools

#### Scenario: Disabled tools are omitted
- **WHEN** a registered tool is disabled by policy
- **THEN** AetherSoul SHALL omit it from the LLM request tool list

### Requirement: Runtime tool audit

The system SHALL provide a runtime audit of visible, disabled, and missing OpenClaw-parity tools.

#### Scenario: Audit reports visible tools
- **WHEN** the audit is requested
- **THEN** it SHALL include the total visible tool count and sorted visible tool names

#### Scenario: Audit reports disabled tools
- **WHEN** a tool exists but is disabled by policy
- **THEN** the audit SHALL include the disabled tool and a concise reason

### Requirement: OpenClaw migration baseline tools

Aether SHALL provide a baseline set of familiar OpenClaw-era tools for migrated agents: `web_search`, `web_fetch`, `memory_read`, `memory_write`, `memory_search`, `skill_list`, `skill_read`, `session_status`, `session_reset`, `shell`, and policy-gated `exec`.

#### Scenario: Migration baseline visible
- **WHEN** Maria's default workspace is loaded with default safe policy
- **THEN** safe baseline tools SHALL be visible to the LLM
- **AND** unsafe or owner-only aliases such as `exec` SHALL be omitted or disabled unless explicitly enabled

### Requirement: Workspace path restriction enforcement

Baseline tools that access files SHALL enforce workspace path restrictions. Attempts to access paths outside the workspace or containing path traversal SHALL be rejected with a model-readable error.

#### Scenario: skill_read rejects path traversal
- **WHEN** `skill_read` is called with a name containing `..` or path separators
- **THEN** the tool SHALL reject the call with an `UnauthorizedAccessException` without reading any file

#### Scenario: memory_read rejects paths outside memory/
- **WHEN** `memory_read` is called with a path that resolves outside the workspace `memory/` directory
- **THEN** the tool SHALL reject the call with an `UnauthorizedAccessException`

#### Scenario: memory_write rejects writes when sandbox denies writes
- **WHEN** `memory_write` is called and `SandboxContext.AllowWrites` is `false`
- **THEN** the tool SHALL reject the call with an `UnauthorizedAccessException`

#### Scenario: memory_write rejects paths outside memory/
- **WHEN** `memory_write` is called with a path that resolves outside the workspace `memory/` directory
- **THEN** the tool SHALL reject the call with an `UnauthorizedAccessException`

#### Scenario: memory_search only returns results from permitted paths
- **WHEN** `memory_search` is called and the workspace `memory/` contains files in subdirectories
- **THEN** only files where `SandboxContext.IsPathAllowed` returns `true` SHALL be included in results
