## ADDED Requirements

### Requirement: /new creates a new session
The system SHALL create a new session via `ISessionManager` and clear the ephemeral memory context via `IMemorySystem`. The response SHALL include the new session identifier.

#### Scenario: New session created
- **WHEN** user sends "/new"
- **THEN** a new session ID is generated, ephemeral context is cleared, and response contains "New session: <id>"

#### Scenario: Existing session unaffected
- **WHEN** user sends "/new"
- **THEN** the previous session's message history is preserved in the database

### Requirement: /reset clears context
The system SHALL clear the ephemeral context and recent message history for the current session without creating a new session ID. The response SHALL confirm clearance.

#### Scenario: Context cleared
- **WHEN** user sends "/reset"
- **THEN** ephemeral context is emptied, recent messages are cleared, and response confirms "Context cleared"
