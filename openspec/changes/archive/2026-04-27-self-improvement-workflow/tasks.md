## 1. Data Model and Interface Foundations

- [x] 1.1 Create `CandidateState` enum in `SelfImprovement/` (PROPOSED, APPLIED, VERIFIED, FAILED)
- [x] 1.2 Create `ISelfImprovementService` interface with `RunDailyReviewAsync(CancellationToken)`
- [x] 1.3 Create `IBenchmarkGate` interface with `RunTestsAsync(CancellationToken)` returning exit code and output
- [x] 1.4 Create `IPipelineTracker` interface with `TrackAsync`, `TransitionAsync`, `GetCandidatesAsync`, `GetByStateAsync`
- [x] 1.5 Add `GetRecentSessionsAsync(DateTime since)` to `IMemorySystem` interface
- [x] 1.6 Add `GeneratePatchAsync(string skillName, PromotionCandidate)` to `ISkillEvolution` interface
- [x] 1.7 Add `pipeline_states` table to `AetherDb` schema migration (id, candidate_hash, state, source, content, created_at, updated_at)

## 2. Pipeline Tracker Implementation

- [x] 2.1 Implement `PipelineTracker` with SQLite-backed state persistence
- [x] 2.2 Implement `TrackAsync` — insert new candidate with state PROPOSED
- [x] 2.3 Implement `TransitionAsync` — update state and log transition with timestamp
- [x] 2.4 Implement `GetCandidatesAsync` — return all tracked candidates
- [x] 2.5 Implement `GetByStateAsync` — filter candidates by state
- [x] 2.6 Wire `PipelineTracker` into `AetherDb` for connection sharing

## 3. Benchmark Gate Implementation

- [x] 3.1 Implement `BenchmarkGate` — spawn `dotnet test` with configurable timeout
- [x] 3.2 Capture stdout and stderr from test process
- [x] 3.3 Implement timeout handling — kill process if exceeded
- [x] 3.4 Wire into DI with configurable `TestProjectPath` and `TimeoutSeconds`

## 4. Skill Evolution — Patch Generation

- [x] 4.1 Implement `SkillEvolution.GeneratePatchAsync` — write structured patch file to `patches/`
- [x] 4.2 Format patch with metadata header (date, confidence, evidence_count, source, state)
- [x] 4.3 Include Issue section and Proposed Change section in patch body
- [x] 4.4 Ensure `patches/` directory exists before writing, create if missing
- [x] 4.5 Update `SkillEvolution` DI registration to pass patches path

## 5. Self-Improvement Service (Pipeline Orchestrator)

- [x] 5.1 Implement `SelfImprovementService` constructor injecting `IMemorySystem`, `ISkillEvolution`, `IBenchmarkGate`, `IPipelineTracker`, `ILogger`
- [x] 5.2 Implement reflection generation phase — query recent sessions, detect corrections/errors/refusals
- [x] 5.3 Implement promotion phase — collect candidates, call `TryPromoteAsync`, track transitions
- [x] 5.4 Implement recidivism phase — call `GetRecidivismCandidatesAsync`, generate patches for those meeting threshold
- [x] 5.5 Implement benchmark phase — run tests via `IBenchmarkGate`, transition patches to VERIFIED or FAILED
- [x] 5.6 Implement surfacing phase — log VERIFIED patches prominently for human review
- [x] 5.7 Implement per-phase exception handling — any phase failure logs and continues to next phase

## 6. Daily Review Cron

- [x] 6.1 Implement `DailyReviewHostedService` as `BackgroundService`
- [x] 6.2 Implement 1-minute polling timer that checks if current UTC time is within midnight window
- [x] 6.3 Call `ISelfImprovementService.RunDailyReviewAsync` when midnight window hit
- [x] 6.4 Implement exception handling so cron survives transient failures
- [x] 6.5 Log "daily review started" and "daily review completed" with duration

## 7. DI Wiring

- [x] 7.1 Register `IBenchmarkGate`, `BenchmarkGate` as singleton in Program.cs
- [x] 7.2 Register `IPipelineTracker`, `PipelineTracker` as singleton
- [x] 7.3 Register `ISelfImprovementService`, `SelfImprovementService` as singleton
- [x] 7.4 Register `DailyReviewHostedService` as `IHostedService`
- [x] 7.5 Pass `patches/` path from configuration or default to app-relative directory
- [x] 7.6 Create `patches/.gitkeep` file

## 8. Memory Interface Updates

- [x] 8.1 Implement `GetRecentSessionsAsync` in `SqliteMemorySystem` — query sessions with activity since given UTC timestamp
- [x] 8.2 Add stub `GetRecentSessionsAsync` returning empty in `FileMemory`

## 9. Tests

- [x] 9.1 Test: `PipelineTracker` tracks new candidate with PROPOSED state
- [x] 9.2 Test: `PipelineTracker` transitions state from PROPOSED to APPLIED
- [x] 9.3 Test: `PipelineTracker` filters by state correctly
- [x] 9.4 Test: `BenchmarkGate` returns success when `dotnet test` exits 0 (mock process)
- [x] 9.5 Test: `BenchmarkGate` returns failure when process exits non-zero
- [x] 9.6 Test: `BenchmarkGate` kills process on timeout
- [x] 9.7 Test: `SkillEvolution.GeneratePatchAsync` writes file with correct format
- [x] 9.8 Test: `SelfImprovementService` executes all phases in order
- [x] 9.9 Test: `SelfImprovementService` continues to next phase when one phase throws
- [x] 9.10 Test: `DailyReviewHostedService` triggers review at midnight UTC
- [x] 9.11 Test: `GetRecentSessionsAsync` returns sessions from last 24h
- [x] 9.12 Test: `SqliteMemorySystem.TryPromoteAsync` rejects candidate below confidence threshold
