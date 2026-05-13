## ADDED Requirements

### Requirement: OnMemoryWrite Hook Fired on Memory Operations

When content is written to any memory layer (ephemeral buffer, working SQLite, or durable MEMORY.md), the system SHALL fire `HookPoint.OnMemoryWrite` using `HookEngine.RunAllAsync`. The hook SHALL receive an `OnMemoryWriteContext` with `MemoryLayer` ("ephemeral"/"working"/"durable"), `Content`, and `Confidence`. The hook SHALL be able to deny the write.

#### Scenario: Hook observes ephemeral memory write
- **WHEN** `FileMemory.AddToContext(content, confidence)` is called
- **THEN** `OnMemoryWrite` SHALL fire with `MemoryLayer = "ephemeral"` and the content

#### Scenario: Hook denies durable memory write
- **WHEN** an OnMemoryWrite hook sets `Denied = true` and `DenyReason = "duplicate content"`
- **AND** the write targets `MemoryLayer = "durable"`
- **THEN** the content SHALL NOT be written to MEMORY.md

### Requirement: OnMemoryPromote Hook Fired on Promotion Candidates

When a `PromotionCandidate` is considered for promotion to durable memory, the system SHALL fire `HookPoint.OnMemoryPromote`. The hook SHALL receive the candidate's content, confidence score, and evidence count.

#### Scenario: Hook observes promotion
- **WHEN** `TryPromoteAsync` is called with a candidate meeting confidence ≥ 0.7 and evidence ≥ 3
- **THEN** `OnMemoryPromote` SHALL fire before the write to MEMORY.md
