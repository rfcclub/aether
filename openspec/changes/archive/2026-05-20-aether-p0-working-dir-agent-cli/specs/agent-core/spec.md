# agent-core Specification (Delta)

## Purpose

Modify existing agent-core requirements to support the new working directory and config hierarchy. Agent profile resolution now reads from `~/.aether/workspaces/<name>/` with a backward-compatible fallback.

## MODIFIED Requirements

### Requirement: Host startup initializes all subsystems

The `IHostedService` start sequence SHALL call `WorkingDirectoryInitializer.InitializeAsync()`, then `AetherDb.InitializeAsync()`, then `SqliteMemorySystem.InitializeAsync()`, and tool registry startup in order before accepting messages. Working directory initialization SHALL complete before any database or provider operation.

#### Scenario: Clean startup completes without error

- **WHEN** the host starts with valid configuration
- **THEN** all initialization steps SHALL complete in order (working dir → database → memory → tools) and the agent SHALL be ready to receive messages within 5 seconds

#### Scenario: Working directory initialization failure halts startup

- **WHEN** `WorkingDirectoryInitializer.InitializeAsync()` throws (e.g., permission denied)
- **THEN** the host SHALL log the error and stop — no database or provider initialization SHALL occur

#### Scenario: Database initialization failure halts startup

- **WHEN** `AetherDb.InitializeAsync()` throws
- **THEN** the host SHALL log the error and stop (not swallow the exception)

### Requirement: DI container has no duplicate registrations

`Program.cs` SHALL register each service interface exactly once. The new `ConfigLoader`, `WorkingDirectoryInitializer`, and `AgentWorkspaceScaffolder` services SHALL be registered as singletons. The duplicate `IToolExecutor` registration present in the skeleton SHALL be removed.

#### Scenario: Correct DI graph resolves all services

- **WHEN** the DI container is built
- **THEN** resolving `IToolExecutor`, `IMemorySystem`, `ILLMProvider`, `ISessionManager`, `AetherSoul`, `ConfigLoader`, `WorkingDirectoryInitializer`, and `IAgentProfile` SHALL each return a single, correctly wired instance

### Requirement: AetherSoul injects skill context into system prompt

When the Skill System is enabled, `AetherSoul` SHALL call `ISkillRegistry.GetActiveSkills(prompt)` before building the system prompt and append matched skills. The agent's persona SHALL be loaded from `~/.aether/workspaces/<name>/` via `IAgentProfile.LoadPersonaAsync()` and prepended before skill context.

#### Scenario: Skill matched and injected with agent persona

- **WHEN** a user prompt triggers a skill for agent `maria`
- **THEN** the system prompt SHALL include the agent persona (from workspace files) followed by `## Skill: <name>\n<body>`

#### Scenario: No skills matched

- **WHEN** no skill matches the user prompt
- **THEN** the system prompt SHALL be built without any skill section (no empty headers)

### Requirement: Tool loop max iterations configurable

`AetherSoul` SHALL respect a configurable `agent:max_tool_iterations` setting (default 8) sourced from the merged configuration hierarchy. Exceeding it SHALL return a graceful error response rather than throwing an exception to the gateway.

#### Scenario: Max iterations reached

- **WHEN** the tool loop runs for `max_tool_iterations` without a final text response
- **THEN** `AetherSoul` SHALL return `AgentResponse` with content `"Agent exceeded maximum tool iterations. Please try a simpler request."` and SHALL NOT propagate an exception

#### Scenario: Custom iteration limit from agent config

- **WHEN** `max_tool_iterations = 4` is set in the agent's `.aether.json`
- **THEN** the loop SHALL stop after 4 iterations for that agent only

## ADDED Requirements

### Requirement: Agent profile resolves from ~/.aether/workspaces first

`AgentProfile` SHALL resolve the agent workspace directory from `~/.aether/workspaces/<name>/` when available. If the workspace does not exist there, it SHALL fall back to `<cwd>/agents/<name>/` with a deprecation warning logged.

#### Scenario: Workspace found in ~/.aether

- **WHEN** `~/.aether/workspaces/maria/` exists and agent `maria` is resolved
- **THEN** `AgentProfile` SHALL load files from `~/.aether/workspaces/maria/` with no warning

#### Scenario: Fallback to repo-relative agents directory

- **WHEN** `~/.aether/workspaces/maria/` does not exist but `<cwd>/agents/maria/` does
- **THEN** `AgentProfile` SHALL load files from `<cwd>/agents/maria/` and log: "[DEPRECATED] Agent 'maria' loaded from repo-relative path. Migrate to ~/.aether/workspaces/maria/."

#### Scenario: Neither path exists

- **WHEN** neither `~/.aether/workspaces/maria/` nor `<cwd>/agents/maria/` exists
- **THEN** `AgentProfile` SHALL throw `DirectoryNotFoundException` with message: "Agent workspace for 'maria' not found."

### Requirement: WorkingDirectoryInitializer registered as IHostedService

`WorkingDirectoryInitializer` SHALL implement `IHostedService` and be registered before `AetherInitializationService` in the startup pipeline. It SHALL be the first service to run.

#### Scenario: WD initializer runs first

- **WHEN** Aether host starts
- **THEN** `WorkingDirectoryInitializer.StartAsync()` SHALL be called before `AetherInitializationService.StartAsync()`

#### Scenario: WD initializer is idempotent on restart

- **WHEN** Aether host starts and `~/.aether/` already exists
- **THEN** `WorkingDirectoryInitializer.StartAsync()` SHALL complete without modifying any files and without error
