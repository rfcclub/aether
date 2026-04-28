## ADDED Requirements

### Requirement: Pipeline orchestrates reflection, promotion, recidivism, and patches
The self-improvement pipeline SHALL execute phases in order: scan sessions for friction → generate reflections → run promotion for eligible candidates → check recidivism for skill failures → generate skill patches → run benchmark gate → surface VERIFIED patches for human review.

#### Scenario: Full pipeline executes end-to-end
- **WHEN** `SelfImprovementService.RunDailyReviewAsync` is called
- **THEN** reflection generation, promotion, recidivism check, and patch generation SHALL execute in sequence
- **THEN** each phase SHALL log its completion with a count of items processed

#### Scenario: One phase fails without blocking others
- **WHEN** the promotion phase throws an exception
- **THEN** subsequent phases (recidivism, patches, benchmark) SHALL still execute
- **THEN** the exception SHALL be logged with phase name and error details

### Requirement: Promotion pipeline processes candidates from all sources
The pipeline SHALL collect `PromotionCandidate` objects from reflection generation and recidivism detection, then attempt promotion for each one via `IMemorySystem.TryPromoteAsync`.

#### Scenario: Candidates from both reflection and recidivism
- **WHEN** reflection generates 2 candidates and recidivism generates 1 candidate
- **THEN** `TryPromoteAsync` SHALL be called for all 3 candidates
- **THEN** each candidate's state SHALL be updated to APPLIED or remain PROPOSED based on the result

#### Scenario: No candidates to promote
- **WHEN** neither reflection nor recidivism generates any candidates
- **THEN** the promotion phase SHALL log an informational message and continue

### Requirement: Pipeline surfaces verified patches for human review
After benchmark verification, any patch marked VERIFIED SHALL be logged prominently so a human operator can review and apply it.

#### Scenario: Verified patch surfaced
- **WHEN** a patch passes benchmark verification
- **THEN** the system SHALL log a message containing the patch file path and a summary of the proposed change
- **THEN** the patch state SHALL be set to VERIFIED

#### Scenario: Failed patch not surfaced
- **WHEN** a patch fails benchmark verification
- **THEN** the system SHALL log the failure with the patch path and exit code
- **THEN** the patch state SHALL be set to FAILED
- **THEN** the system SHALL NOT surface the patch for human review

### Requirement: Pipeline runs on demand in addition to daily cron
The `ISelfImprovementService` interface SHALL expose `RunDailyReviewAsync` which can be called from the daily cron or invoked directly for manual review.

#### Scenario: Manual review triggered
- **WHEN** `RunDailyReviewAsync` is called directly (not from cron)
- **THEN** the full pipeline SHALL execute identically to a cron-triggered run
