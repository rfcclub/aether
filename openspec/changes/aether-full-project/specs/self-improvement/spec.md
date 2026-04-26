## ADDED Requirements

### Requirement: Daily review cron captures friction signals
The self-improvement system SHALL run a daily review at midnight UTC as an `IHostedService`, inspecting session history for corrections, refusals, and repeated failures.

#### Scenario: Daily review writes reflections
- **WHEN** the daily cron fires and session history contains messages where the assistant was corrected
- **THEN** the system SHALL write a `patches/reflections-<date>.md` file with identified friction points

#### Scenario: Daily review with no session data
- **WHEN** no sessions exist for the prior day
- **THEN** the system SHALL write an empty reflections file and log an informational message

### Requirement: Promotion pipeline elevates candidates to MEMORY.md
High-confidence, well-evidenced observations SHALL be promoted from working memory to durable memory via `TryPromoteAsync`.

#### Scenario: Candidate meets promotion threshold
- **WHEN** a `PromotionCandidate` has `confidence ≥ 0.7` and `evidence_count ≥ 3`
- **THEN** the system SHALL append the candidate to `MEMORY.md` (if within char limit) and mark it `APPLIED`

#### Scenario: MEMORY.md at capacity
- **WHEN** adding a candidate would exceed the 2,500-char durable limit
- **THEN** the system SHALL run `ForceConsolidationAsync` to merge/evict lowest-confidence entries before promoting

### Requirement: Skill evolution patches from recurring issues
When the same failure pattern appears 3+ times across sessions, the system SHALL generate a SKILL.md patch proposal.

#### Scenario: Recurring failure generates patch
- **WHEN** the recidivism tracker detects the same failure fingerprint 3 times
- **THEN** the system SHALL write a proposed SKILL.md edit to `patches/skill-patch-<name>-<date>.md`

#### Scenario: Patch not auto-applied
- **WHEN** a skill patch is generated
- **THEN** the system SHALL NOT modify any file in `skills/` automatically; the patch SHALL remain in `patches/` until human review

### Requirement: Benchmark gate before patch promotion
Before any patch is ready for human review, the system SHALL run the project's test suite and smoke test.

#### Scenario: Tests pass
- **WHEN** `dotnet test` and `Aether.exe --smoke` both exit 0 on the patched code
- **THEN** the patch SHALL be marked `VERIFIED` and the human-review prompt SHALL be emitted to the log

#### Scenario: Tests fail
- **WHEN** any benchmark check exits non-zero
- **THEN** the patch SHALL be marked `FAILED` and SHALL NOT be surfaced for human review

### Requirement: Improvement pipeline is inspectable via visibility layer
All candidates SHALL have a visible state: `PROPOSED`, `APPLIED`, `VERIFIED`, or `FAILED`.

#### Scenario: State transitions logged
- **WHEN** a candidate moves between states
- **THEN** the system SHALL write a log entry with timestamp, candidate ID, previous state, and new state
