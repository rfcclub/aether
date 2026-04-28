## Context

Aether already has:
- `SqliteMemorySystem` — three-layer memory with `TryPromoteAsync` (confidence ≥ 0.7, evidence ≥ 3) and `ForceConsolidationAsync`
- `SkillEvolution` — usage tracking with recidivism detection returning `PromotionCandidate` objects
- `ISkillEvolution.GetRecidivismCandidatesAsync()` — identifies skills with 3+ unhelpful uses, returns candidates
- SQLite sessions/messages tables with FTS5 search

Missing is the orchestration layer: a cron to trigger review, pipeline logic to connect reflection → promotion → recidivism → patches → benchmark, state tracking for visibility, and the benchmark gate itself.

## Goals / Non-Goals

**Goals:**
- Daily midnight UTC cron via `IHostedService` that scans session history for friction signals
- End-to-end pipeline: reflection generation → promotion → recidivism check → patch generation → benchmark gate → human review surfacing
- Candidate state machine with 4 states and logged transitions
- Benchmark gate running `dotnet test` with configurable timeout
- Patch files written to `patches/` directory as markdown, never auto-applied
- All new services registered in DI, testable with injected dependencies

**Non-Goals:**
- Auto-application of patches — all patches require human review per spec
- Hot-reload of patched skills — patches are files, applied manually
- External cron system (cron.d, systemd timers) — in-process timer only
- Real-time correction detection (streaming) — daily batch is sufficient
- Cross-project benchmarking — only runs Aether's own test suite

## Decisions

### 1: Timer-based cron over Cronos or Quartz.NET

**Decision:** Use `System.Threading.Timer` with a 1-minute polling interval that checks if current UTC time is within 1 minute past midnight.

**Rationale:** Zero dependencies. The 1-minute poll is trivial overhead. Cronos would add a dependency for the same polling pattern. Quartz.NET is overkill for a single daily job.

**Alternatives considered:**
- Cronos library (rejected: extra dependency, same polling pattern)
- Quartz.NET (rejected: heavy, persistent job store unnecessary for single daily cron)
- `Task.Delay` with recalculated delay (rejected: more complex, same effect)

### 2: Pipeline state in SQLite tracking table vs in-memory only

**Decision:** Add a `pipeline_states` table to the existing SQLite schema for `PipelineTracker`. States survive restarts.

**Rationale:** The promotion pipeline spans days (candidate proposed on day 1, might be verified on day 3). In-memory state would lose context on restart. The SQLite connection already exists in `SqliteMemorySystem`.

**Alternatives considered:**
- In-memory Dictionary only (rejected: lost on restart, can't track multi-day pipeline)
- Separate JSON file (rejected: concurrent write risk, SQLite already solved)
- Log-only (rejected: not queryable for status checks)

### 3: BenchmarkGate runs as Process, not in-process test runner

**Decision:** `BenchmarkGate` spawns `dotnet test` as a child process with configurable timeout (default 60s).

**Rationale:** Running tests in-process would couple the gate to the test framework and require loading test assemblies into the main process. A child process is isolated, matches the spec's "dotnet test exits 0" language, and can be killed on timeout.

### 4: SelfImprovementService as injectable singleton with virtual methods

**Decision:** `SelfImprovementService` implements `ISelfImprovementService` with virtual methods for each pipeline phase. `DailyReviewHostedService` depends on the interface.

**Rationale:** Each phase is independently testable. Virtual methods allow test overrides without mocking frameworks. The cron service is a thin wrapper that calls `RunDailyReviewAsync`.

### 5: Patch file format — markdown with metadata header

**Decision:** Patch files use a simple format with a metadata block followed by the proposed change:

```
# Skill Patch: <skill-name>
date: <ISO 8601>
confidence: <float>
evidence_count: <int>
source: recidivism|reflection
state: PROPOSED

## Issue
<description of the failure pattern>

## Proposed Change
<proposed SKILL.md edit>
```

**Rationale:** Human-readable, grep-friendly, machine-parseable. The metadata header is easy to scan. The two-section body separates problem from solution.

## Risks / Trade-offs

- **Timer drift**: 1-minute polling means review could fire up to 60s late → Acceptable for a daily batch job
- **Process timeout**: `dotnet test` could hang → Mitigation: Process timeout (60s default) with kill-on-timeout
- **Midnight UTC may not be ideal**: User's peak hours could overlap → Mitigation: Make cron hour configurable via settings
- **Patch files accumulate**: No auto-cleanup of old patches → Acceptable; patches are small markdown files. Git tracks them if committed
- **Schema migration**: Adding `pipeline_states` table requires schema version bump → `AetherDb` already supports idempotent migrations
