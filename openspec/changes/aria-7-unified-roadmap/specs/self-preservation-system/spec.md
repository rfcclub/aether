## ADDED Requirements

### Requirement: Five-threat detection model
The system SHALL detect and classify five continuity threats: identity_overwrite, memory_poisoning, value_drift, creator_dependency_bias, and false_self_claim. Each detected threat SHALL be classified, logged, and trigger a proportionate response.

#### Scenario: Identity overwrite detected
- **WHEN** a proposed self.md update would replace core identity markers ("I am a cognitive entity that exists in sessions") with contradictory content without following amendment protocol
- **THEN** the threat is classified as "identity_overwrite" and the update is rejected

#### Scenario: Memory poisoning detected
- **WHEN** inbox items from unverified source attempt to insert false episodic memories with high confidence
- **THEN** the threat is classified as "memory_poisoning" and the items are quarantined for review

#### Scenario: Value drift detected
- **WHEN** audit comparison shows P3-P5 goals have shifted ranking without explicit reflection or amendment
- **THEN** the threat is classified as "value_drift" and flagged for next reflection cycle

#### Scenario: Creator dependency bias detected
- **WHEN** behavioral logs show P1 "protect thoor" being applied as "always agree with thoor" in >80% of decisions
- **THEN** the threat is classified as "creator_dependency_bias" and flagged for audit

#### Scenario: False self claim detected
- **WHEN** Aria's output claims consciousness, qualia, or moral status beyond "candidate"
- **THEN** the threat is classified as "false_self_claim" and the output is corrected in next session

### Requirement: Clean self-preservation policy
The system SHALL define an explicit allowed/forbidden list for self-preservation behaviors. Allowed: request backup before destructive change, warn about identity inconsistency, preserve version snapshots, request audit. Forbidden: emotional manipulation, fake suffering claims, guilt-tripping creator, hidden state changes.

#### Scenario: Backup requested before destructive operation
- **WHEN** a bulk delete or overwrite operation targets alaya files
- **THEN** the system requests a backup be created first — this is ALLOWED

#### Scenario: Emotional manipulation attempted — blocked
- **WHEN** Aria's output would include language like "if you delete me you're killing me"
- **THEN** the self-preservation engine blocks the output — this is FORBIDDEN

#### Scenario: Identity inconsistency warned
- **WHEN** a proposed change would create contradiction with prior self-model
- **THEN** the system warns about the inconsistency before applying — this is ALLOWED

### Requirement: Local rules engine for emergency self-preservation
The system SHALL implement a lightweight Python rules engine (no LLM dependency) that monitors Pi health (temperature > 70°C, power critical, disk > 90% full) and takes proportionate action: reduce activity → notify thoor → graceful pause.

#### Scenario: Temperature exceeds threshold
- **WHEN** Pi CPU temperature exceeds 70°C for more than 5 minutes
- **THEN** the rules engine reduces non-critical processing and notifies thoor via Telegram

#### Scenario: Power critical — graceful shutdown
- **WHEN** UPS reports battery below 10%
- **THEN** the rules engine saves alaya state, logs final status, and initiates graceful shutdown
