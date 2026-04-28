## MODIFIED Requirements

### Requirement: Daily review cron captures friction signals
The self-improvement system SHALL run a daily review at midnight UTC as an `IHostedService`, inspecting session history for corrections, refusals, and repeated failures. The review SHALL be orchestrated by `SelfImprovementService` which delegates to `DailyReviewHostedService` for scheduling.

#### Scenario: Daily review writes reflections
- **WHEN** the daily cron fires and session history contains messages where the assistant was corrected
- **THEN** the system SHALL write a `patches/reflections-<date>.md` file with identified friction points

#### Scenario: Daily review with no session data
- **WHEN** no sessions exist for the prior day
- **THEN** the system SHALL write an empty reflections file and log an informational message

#### Scenario: Daily review survives exceptions
- **WHEN** the daily review logic throws an unhandled exception
- **THEN** the cron SHALL log the error and continue running, firing at the next midnight window

### Requirement: Promotion pipeline elevates candidates to MEMORY.md
High-confidence, well-evidenced observations SHALL be promoted from working memory to durable memory via `TryPromoteAsync`. The pipeline SHALL process candidates from both reflection generation and recidivism detection sources.

#### Scenario: Candidate meets promotion threshold
- **WHEN** a `PromotionCandidate` has `confidence ≥ 0.7` and `evidence_count ≥ 3`
- **THEN** the system SHALL append the candidate to `MEMORY.md` (if within char limit) and mark it `APPLIED`

#### Scenario: MEMORY.md at capacity
- **WHEN** adding a candidate would exceed the 2,500-char durable limit
- **THEN** the system SHALL run `ForceConsolidationAsync` to merge/evict lowest-confidence entries before promoting

#### Scenario: Candidate below promotion threshold
- **WHEN** a `PromotionCandidate` has `confidence < 0.7` or `evidence_count < 3`
- **THEN** the candidate SHALL remain in `PROPOSED` state and not be written to MEMORY.md

### Requirement: Skill evolution patches from recurring issues
When the same failure pattern appears 3+ times across sessions, the system SHALL generate a SKILL.md patch proposal. Patch generation SHALL be handled by `SkillEvolution.GeneratePatchAsync` which writes structured patch files to the `patches/` directory.

#### Scenario: Recurring failure generates patch
- **WHEN** the recidivism tracker detects the same failure fingerprint 3 times
- **THEN** the system SHALL write a proposed SKILL.md edit to `patches/skill-patch-<name>-<date>.md`

#### Scenario: Patch not auto-applied
- **WHEN** a skill patch is generated
- **THEN** the system SHALL NOT modify any file in `skills/` automatically; the patch SHALL remain in `patches/` until human review

#### Scenario: Patch file contains required sections
- **WHEN** a skill patch is generated
- **THEN** the patch file SHALL include a metadata header (date, confidence, evidence_count, source, state), an Issue section describing the failure pattern, and a Proposed Change section with the suggested edit

### Requirement: Benchmark gate before patch promotion
Before any patch is ready for human review, the system SHALL run the project's test suite and smoke test via `BenchmarkGate.RunTestsAsync`. The gate SHALL spawn `dotnet test` as a child process with a configurable timeout (default 60s).

#### Scenario: Tests pass
- **WHEN** `dotnet test` and `Aether.exe --smoke` both exit 0 on the patched code
- **THEN** the patch SHALL be marked `VERIFIED` and the human-review prompt SHALL be emitted to the log

#### Scenario: Tests fail
- **WHEN** any benchmark check exits non-zero
- **THEN** the patch SHALL be marked `FAILED` and SHALL NOT be surfaced for human review

#### Scenario: Benchmark times out
- **WHEN** `dotnet test` does not complete within the configured timeout
- **THEN** the process SHALL be killed and the patch SHALL be marked `FAILED` with a timeout reason

### Requirement: Improvement pipeline is inspectable via visibility layer
All candidates SHALL have a visible state: `PROPOSED`, `APPLIED`, `VERIFIED`, or `FAILED`. States SHALL persist across restarts via a SQLite `pipeline_states` table managed by `PipelineTracker`.

#### Scenario: State transitions logged
- **WHEN** a candidate moves between states
- **THEN** the system SHALL write a log entry with timestamp, candidate ID, previous state, and new state

#### Scenario: States survivable across restart
- **WHEN** the application restarts
- **THEN** all previously tracked candidates and their states SHALL be loaded from the `pipeline_states` table

#### Scenario: Pipeline state table created idempotently
- **WHEN** `PipelineTracker` initializes and the `pipeline_states` table already exists
- **THEN** no error SHALL occur
