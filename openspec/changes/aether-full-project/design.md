## Context

Aether is a .NET 9 agent runtime. The core conversation loop (`AetherSoul`), session persistence (SQLite via `AetherDb`), message routing (`MessageRouter`), LLM provider abstraction (`OpenRouterProvider`/`ProviderRouter`), and tool registry skeletons exist. Nothing is fully wired end-to-end: the host never receives an external message, memory is never written, tools are never validated, and no channels are open.

Current defects in the skeleton:
- `IToolExecutor` registered twice in DI (Program.cs)
- `AetherDb.InitializeAsync()` and `SqliteMemorySystem.InitializeAsync()` never called at startup
- `ProviderRouter` has two different `ILLMProvider` contract shapes in the codebase (`ChatAsync` in router, `CompleteAsync` in AetherSoul) — needs reconciliation

## Goals / Non-Goals

**Goals:**
- Ship a running agent accessible over WebSocket that remembers context across sessions
- Complete all skeleton implementations (memory, provider router, tool system)
- Add Telegram channel adapter (already in the channel abstraction)
- Add Skill System (SKILL.md-driven procedural capabilities)
- Add Self-Improvement workflow (6-phase pipeline, no auto-commit)
- Add multi-agent gateway routing (OpenClaw-style: config-selected agent per channel)
- Fix all DI/startup bugs

**Non-Goals:**
- vLLM/Ollama local provider (infrastructure dependency, deferred)
- Python RPC bridge for ML.NET optimization (deferred)
- Auto-commit of self-improvement patches (always human-gated)
- gRPC gateway (WebSocket + Telegram covers all current use cases)

## Decisions

### D1: Unified LLM contract — `CompleteAsync` everywhere
`AetherSoul` calls `_llm.CompleteAsync(LlmRequest, ct)`. `ProviderRouter` currently exposes `ChatAsync(messages, options, ct)`. **Decision**: rename router to implement `CompleteAsync` from `ILLMProvider` (same interface as OpenRouterProvider already uses from the Agent namespace). Single contract, no adapter shim.

*Alternative*: adapter shim wrapping `ChatAsync` → rejected, adds indirection with no benefit.

### D2: FTS5 via virtual table, BM25 native
SQLite FTS5 `matchinfo()` + `bm25()` are built-in on `Microsoft.Data.Sqlite` ≥ 8.0 with bundled SQLite. No extra package needed. Schema: `CREATE VIRTUAL TABLE messages_fts USING fts5(content, content=messages, content_rowid=rowid)`.

*Alternative*: Lucene.NET — rejected, ~10 MB extra, overkill for single-user.

### D3: NJsonSchema for tool parameter validation
`NJsonSchema` v11 has `JsonSchema.FromJsonAsync()` + `ValidateAsync()`. Validation happens at `IToolExecutor.ExecuteAsync()` before dispatch. Invalid input returns `ToolResult.Failure("schema validation failed: ...")`.

*Alternative*: `System.Text.Json.Schema` (preview, .NET 9) — API unstable as of 2026-04; use NJsonSchema for now.

### D4: Skill triggers — exact match first, then cosine fallback
Skills can trigger via explicit `/skill-name` command OR description similarity. Explicit match is O(1) dictionary lookup. Similarity check uses precomputed TF-IDF vector (computed at load time) vs. user prompt, threshold 0.65. If no provider available for similarity (cold start), skip auto-trigger, require explicit command.

### D5: Self-improvement — no auto-commit, PR only
All patches from the improvement pipeline are written to a `patches/` directory and require `git diff` + human approval before merge. The benchmark gate (unit tests + regression suite) must pass before PR is opened. `DailyReviewCron` runs at midnight UTC via `IHostedService`.

### D6: Multi-agent routing — config-selected, not channel-inferred
Agent selection is explicit in `appsettings.json` per channel source. `gateway.agents["telegram"] = "aria"`, `gateway.agents["websocket"] = "default"`. The router resolves the agent name → `AetherSoul` instance from DI keyed services. No ML-based routing.

### D7: Memory compaction — priority eviction, not FIFO
Ephemeral buffer evicts lowest-priority messages first (priority = recency × importance_score). Importance score: tool_result > assistant > user (default). FIFO is a degraded fallback when scores are equal.

## Risks / Trade-offs

- [FTS5 content table sync] — `messages_fts` is a content table; inserts need explicit `INSERT INTO messages_fts(rowid, content)` trigger or manual sync. → Mitigation: add SQLite `AFTER INSERT` trigger in Schema.sql.
- [ProviderRouter contract mismatch] — touching the provider interface risks breaking existing `OpenRouterProvider`. → Mitigation: rename `ChatAsync` → `CompleteAsync` in one PR, compile-verify all providers.
- [Self-improvement patches race condition] — if two daily reviews run concurrently (restart during cron), patches directory could have conflicts. → Mitigation: file-based lock (`patches/.lock`) held for write duration.
- [WebSocket reconnect state] — sessions tied to `groupFolder` (string key). WebSocket client reconnect gets new connection but same `groupFolder` key → same session. No issue, by design.
- [Skill similarity at cold start] — TF-IDF build requires all skills loaded. Slow disk or empty skills dir should not block agent startup. → Mitigation: lazy-build on first trigger check, not at startup.

## Migration Plan

1. Fix DI bugs (duplicate `IToolExecutor`, missing `InitializeAsync`) — standalone commit, no behavior change
2. Reconcile LLM contract (`CompleteAsync` everywhere) — compile-verified, all providers updated
3. Complete SQLite schema (FTS5 triggers, promotion_candidates table) — additive, safe to apply
4. Complete Memory, Tool, Provider implementations — no new APIs, fills `NotImplementedException` stubs
5. Add Gateway (WebSocket + Telegram) — new hosted services, no change to existing code paths
6. Add Skill System — new namespace, injected into `AetherSoul` prompt builder
7. Add Self-Improvement — new hosted service (`DailyReviewCron`), writes to `patches/`, never auto-commits
8. Add Multi-Agent routing — DI keyed services, config-driven

Rollback: each step is additive or isolated. Steps 1-4 can be reverted by reverting the commit. Steps 5-8 can be disabled via config (`gateway.enabled: false`, `skills.enabled: false`, `improvement.enabled: false`).

## Open Questions

- Should Telegram adapter use polling or webhook? (Webhook requires public HTTPS endpoint — polling is simpler for dev, webhook for prod. Decision: polling default, webhook configurable.)
- Benchmark suite for improvement gating: unit tests only, or also integration smoke tests? (Tentative: unit + `--smoke` flag on `Aether.exe`.)
- Multi-agent: should agents share the same SQLite db or have separate db files? (Tentative: same db, separate `group_folder` prefix per agent.)
