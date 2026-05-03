## ADDED Requirements

### Requirement: /context shows session statistics
The system SHALL return current session ID, message count, estimated token usage, active model, and memory layer status when `/context` is sent.

#### Scenario: Show context stats
- **WHEN** user sends "/context"
- **THEN** response includes session ID, message count, estimated tokens, model name, and ephemeral context entry count

#### Scenario: No active session
- **WHEN** user sends "/context" with no prior conversation in this channel
- **THEN** response shows "No active session" or stats with zero counts
