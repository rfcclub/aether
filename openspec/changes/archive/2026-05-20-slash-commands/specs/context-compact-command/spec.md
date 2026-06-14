## ADDED Requirements

### Requirement: /compact compacts context
The system SHALL call `IMemorySystem.CompactContext()` with a default target of 4000 tokens when `/compact` is sent. The response SHALL confirm the new estimated token count.

#### Scenario: Context compacted
- **WHEN** user sends "/compact"
- **THEN** `CompactContext(4000)` is called and response confirms "Context compacted to ~N tokens"

#### Scenario: Compaction with no context
- **WHEN** user sends "/compact" and there is no context to compact
- **THEN** response shows "No context to compact" or "Context already minimal"
