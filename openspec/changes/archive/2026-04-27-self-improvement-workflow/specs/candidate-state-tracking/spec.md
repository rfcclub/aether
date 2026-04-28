## ADDED Requirements

### Requirement: Candidates have four lifecycle states
Every `PromotionCandidate` processed through the pipeline SHALL have one of four states: `PROPOSED`, `APPLIED`, `VERIFIED`, or `FAILED`.

#### Scenario: New candidate starts as PROPOSED
- **WHEN** a `PromotionCandidate` is created from reflection or recidivism
- **THEN** its initial state SHALL be `PROPOSED`

#### Scenario: Successful promotion transitions to APPLIED
- **WHEN** `TryPromoteAsync` returns true for a PROPOSED candidate
- **THEN** the candidate's state SHALL transition to `APPLIED`

#### Scenario: Failed promotion remains PROPOSED
- **WHEN** `TryPromoteAsync` returns false for a PROPOSED candidate due to confidence/evidence thresholds
- **THEN** the candidate's state SHALL remain `PROPOSED`

#### Scenario: Passed benchmark transitions to VERIFIED
- **WHEN** an APPLIED patch passes the benchmark gate
- **THEN** the candidate's state SHALL transition to `VERIFIED`

#### Scenario: Failed benchmark transitions to FAILED
- **WHEN** a patch fails the benchmark gate
- **THEN** the candidate's state SHALL transition to `FAILED`

### Requirement: State transitions are logged
Every state transition SHALL produce a structured log entry containing the candidate ID, timestamp, previous state, and new state.

#### Scenario: Transition logged with required fields
- **WHEN** a candidate transitions from PROPOSED to APPLIED
- **THEN** a log entry SHALL be written containing the candidate's content hash as ID, the UTC timestamp, the previous state PROPOSED, and the new state APPLIED

### Requirement: Pipeline state persists across restarts
Candidate states SHALL be stored in a SQLite `pipeline_states` table so the pipeline can resume tracking after application restart.

#### Scenario: State restored after restart
- **WHEN** the application restarts and `PipelineTracker` initializes
- **THEN** all previously tracked candidates and their states SHALL be loaded from the `pipeline_states` table

#### Scenario: State table created idempotently
- **WHEN** `PipelineTracker` initializes and the `pipeline_states` table already exists
- **THEN** no error SHALL occur
