# agent-core Specification

## Purpose
TBD - created by archiving change aether-full-project. Update Purpose after archive.
## Requirements
### Requirement: Host startup initializes all subsystems
The `IHostedService` start sequence SHALL call `AetherDb.InitializeAsync()`, `SqliteMemorySystem.InitializeAsync()`, and tool registry startup in order before accepting messages.

#### Scenario: Clean startup completes without error
- **WHEN** the host starts with valid configuration
- **THEN** all initialization steps SHALL complete and the agent SHALL be ready to receive messages within 5 seconds

#### Scenario: Database initialization failure halts startup
- **WHEN** `AetherDb.InitializeAsync()` throws
- **THEN** the host SHALL log the error and stop (not swallow the exception)

### Requirement: DI container has no duplicate registrations
`Program.cs` SHALL register each service interface exactly once. The duplicate `IToolExecutor` registration present in the skeleton SHALL be removed.

#### Scenario: Correct DI graph resolves all services
- **WHEN** the DI container is built
- **THEN** resolving `IToolExecutor`, `IMemorySystem`, `ILLMProvider`, `ISessionManager`, and `AetherSoul` SHALL each return a single, correctly wired instance

### Requirement: AetherSoul injects skill context into system prompt
When the Skill System is enabled, `AetherSoul` SHALL call `ISkillRegistry.GetActiveSkills(prompt)` before building the system prompt and append matched skills.

#### Scenario: Skill matched and injected
- **WHEN** a user prompt triggers a skill
- **THEN** the system prompt SHALL include `## Skill: <name>\n<body>` appended after the memory context

#### Scenario: No skills matched
- **WHEN** no skill matches the user prompt
- **THEN** the system prompt SHALL be built without any skill section (no empty headers)

### Requirement: Tool loop max iterations configurable
`AetherSoul` SHALL respect a configurable `agent:max_tool_iterations` setting (default 8). Exceeding it SHALL return a graceful error response rather than throwing an exception to the gateway.

#### Scenario: Max iterations reached
- **WHEN** the tool loop runs for `max_tool_iterations` without a final text response
- **THEN** `AetherSoul` SHALL return `AgentResponse` with content `"Agent exceeded maximum tool iterations. Please try a simpler request."` and SHALL NOT propagate an exception

#### Scenario: Custom iteration limit respected
- **WHEN** `agent:max_tool_iterations = 4` is set in configuration
- **THEN** the loop SHALL stop after 4 iterations, not the default 8

