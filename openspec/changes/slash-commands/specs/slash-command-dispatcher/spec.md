## ADDED Requirements

### Requirement: Slash command dispatch
The system SHALL intercept messages starting with `/` before the LLM pipeline. The dispatcher SHALL parse the command name (first word after `/`) and route to the appropriate handler. Unknown commands SHALL passthrough to the LLM. Non-`/` messages SHALL passthrough immediately.

#### Scenario: Known command is intercepted
- **WHEN** a message "/new" arrives
- **THEN** the dispatcher routes to the session handler and returns a direct response without calling the LLM

#### Scenario: Unknown slash command passes through
- **WHEN** a message "/unknown" arrives
- **THEN** the dispatcher returns null and the message proceeds to the LLM pipeline

#### Scenario: Normal message passes through
- **WHEN** a message "hello" arrives
- **THEN** the dispatcher returns null immediately without allocation

### Requirement: Channel-agnostic interface
The dispatcher SHALL implement `ISlashCommandHandler` with a single method `HandleAsync(SlashCommandContext, CancellationToken)` returning `SlashCommandResult?`. The interface SHALL NOT depend on any channel type, framework type, or transport.

#### Scenario: Interface does not reference channels
- **WHEN** the `ISlashCommandHandler` interface is inspected
- **THEN** it references only `SlashCommandContext`, `SlashCommandResult`, and standard .NET types

#### Scenario: KuroClaw can implement the interface
- **WHEN** a KuroClaw assembly references the interface
- **THEN** it can implement `ISlashCommandHandler` without importing Aether channel types
