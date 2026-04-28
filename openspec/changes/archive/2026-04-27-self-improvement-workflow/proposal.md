## Why

Aether has the memory and skill-evolution foundations (promotion pipeline, recidivism tracking), but lacks the orchestration layer that ties them into a self-improving loop. The daily review cron, patch generation, benchmark gate, and state visibility pipeline are all missing — without them, the system collects friction signals but never acts on them.

## What Changes

- Add `DailyReviewHostedService` — IHostedService that fires at midnight UTC, inspects session history for corrections/refusals/repeated failures, and writes `patches/reflections-<date>.md`
- Add `SelfImprovementService` — orchestrates the pipeline: scan sessions → generate reflections → run promotion → check recidivism → generate patches → benchmark gate → surface for review
- Add patch file generation to `SkillEvolution` — write `patches/skill-patch-<name>-<date>.md` when recidivism threshold hit
- Add `BenchmarkGate` — runs `dotnet test` and smoke check before patches are marked VERIFIED
- Add candidate state tracking (`PROPOSED`, `APPLIED`, `VERIFIED`, `FAILED`) with `CandidateState` enum and `PipelineTracker`
- Wire self-improvement services into DI in Program.cs
- Add `patches/` directory management and `.gitkeep` for patch artifacts

## Capabilities

### New Capabilities
- `daily-review-cron`: Midnight UTC cron that scans session history for friction signals and writes daily reflections
- `self-improvement-pipeline`: End-to-end pipeline orchestrating reflection, promotion, recidivism detection, patch generation, benchmark gating, and human review surfacing
- `benchmark-gate`: Runs test suite and smoke test before patches are marked ready for human review
- `candidate-state-tracking`: State machine for promotion candidates with logged transitions (PROPOSED → APPLIED → VERIFIED or FAILED)

### Modified Capabilities
- `self-improvement`: Existing spec already defines the requirements; this change implements them. Spec updated with clarified Purpose and additional scenarios for the pipeline orchestration and benchmark gate details.

## Impact

- `src/Aether/SelfImprovement/` — new directory: `DailyReviewHostedService.cs`, `SelfImprovementService.cs`, `BenchmarkGate.cs`, `PipelineTracker.cs`, `CandidateState.cs`
- `src/Aether/Skills/SkillEvolution.cs` — add `GeneratePatchAsync` method
- `src/Aether/Memory/IMemorySystem.cs` — add `GetRecentSessionsAsync` method
- `src/Aether/Program.cs` — register new services in DI
- `src/Aether/Aether.csproj` — no new NuGet dependencies needed
- `patches/` directory — created at runtime, gitignored except `.gitkeep`
- `src/Aether.Tests/` — tests for DailyReviewHostedService, SelfImprovementService, BenchmarkGate, PipelineTracker
