# Aether Unified Memory Engine — Design Spec

**Date:** 2026-05-06
**Status:** Approved
**Source:** Research + debate across OpenClaw (3 engines), Claude Code, MemGPT/Letta, Stanford Generative Agents, OMMC/Tinkerclaw

---

## Problem

Aether has 4 memory subsystems (SQLite+FTS5, FileMemory, agent files, SessionManager) but none are wired together. Critical gaps:

- `LoadContextAsync` returns empty string (stub — SQLite FTS5 exists but never queried)
- `ContextAssembler` hardcodes 2-day lookback on memory/ files
- `MEMORY.md` capped at 2500 chars, append-only, no decay
- No cross-session memory recall beyond the 2-day window
- `ScanSessionForFrictionAsync` in self-improvement pipeline only checks `MessageCount > 20`

## Core Data Model

Single unified table replaces ad-hoc `promotion_candidates` + `pipeline_states` + file-based MEMORY.md:

```sql
CREATE TABLE IF NOT EXISTS memory_entries (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL,
    tier TEXT NOT NULL DEFAULT 'working',  -- working | durable | archival
    content TEXT NOT NULL,
    embedding BLOB,                         -- 384-dim f32; NULL until indexed
    importance REAL NOT NULL DEFAULT 0.5,  -- LLM-scored 0-1
    access_count INTEGER NOT NULL DEFAULT 0,
    source_session TEXT,
    created_at TEXT NOT NULL,
    last_accessed TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS memory_events (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL,
    event_type TEXT NOT NULL,  -- insert | promote | compact | reflect
    entry_id TEXT,
    summary TEXT NOT NULL,
    token_count INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL
);

CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
    content, content=memory_entries, content_rowid=rowid
);
```

**Design decision:** `memory_events` is an immutable append-only log (pattern from Gamma proposal + OMMC event sourcing). `memory_entries` is the mutable working set. FTS5 indexes memory content, not raw chat messages — separate concerns.

---

## Retrieval

Hybrid search with MMR + temporal decay. Borrowed patterns:

| Pattern | Source |
|---------|--------|
| Hybrid FTS5 + vector search with MMR rerank | OMMC/Tinkerclaw |
| Temporal decay weighting | OMMC/Tinkerclaw + Stanford Generative Agents |
| 3-axis retrieval: recency + importance + relevance | Stanford Generative Agents |
| Core memory blocks always in context | MemGPT/Letta |
| Auto-context injection per turn | OpenClaw Hyperspell |
| Embedding provider fallback (FTS5 when ONNX unavailable) | OMMC/Tinkerclaw |

**Algorithm** (`MemoryRetriever`):

```
On context assembly:
  1. Core memory: top-3 entries with importance >= 0.8, always injected
  2. Auto-context: silent FTS5 search with prompt keywords, inject top 3
  3. Hybrid rank: BM25*0.3 + cosine_sim*0.4 + importance*0.2 + recency*0.1
  4. MMR rerank: diversify top 50 candidates → top 10
  5. Temporal decay: score * exp(-days_since_last_access / 30)
  6. Token budget: trim to _dynamicTokenBudget (default 2000 chars)
```

FTS5 is queried on every context assembly (fixes `LoadContextAsync` stub). Embedding via ONNX `e5-small-v2` (384-dim) in Phase 3; FTS5-only fallback until then.

---

## Write Path

**Per-turn** (in `AetherSoul.ProcessAsync`):
1. Extract assistant response + prompt as observation
2. `LLMImportanceClassifier(content)` → (importance 0-1, summary, is_core)
3. Insert into `memory_entries` with `tier='working'`
4. Append to `memory_events` (immutable audit log)

**Promotion pipeline** (hosted service, 6h interval):
- working entries with importance ≥ 0.7 AND access_count ≥ 2 → LLM summarize → promote to `tier='durable'`
- Confidence/evidence gates from existing pipeline preserved

---

## Lifecycle

```
working ──(importance≥0.7, access≥2)──→ durable ──(unread>30d)──→ archival ──(LLM cluster, >90d)──→ deleted
```

**Compaction** (pattern from OpenClaw LCM):
- `freshTailCount = 10` — keep last 10 events uncompacted
- Circuit breaker: 2 consecutive LLM failures → skip cycle, retry next

**Session hooks** (pattern from Claude Code):
- `SessionStart`: load core memory blocks + auto-context inject
- `SessionEnd`: write episode summary to `memory_events`
- `PreCompact`: dump summary before WorkingContext compaction

---

## Cross-Agent (Phase 4)

- `agent_id` column on all tables enables per-agent scoping
- `agent_id = '__shared__'` for group-level memory
- `memory_permissions` table for subagent expansion grants (LCM pattern)

---

## Phase Plan

### Phase 1 — Foundation
Fix all known gaps. Zero new dependencies.

**Files:** `Schema.sql`, `MemoryTypes.cs`, `SqliteMemorySystem.cs`, `FileMemory.cs`, `ContextAssembler.cs`

**Outcomes:** `LoadContextAsync` returns real data. FTS5 queried. 2-day lookback → 30-day. `memory_entries` table active.

### Phase 2 — Retrieval + Ranking
MMR, temporal decay, LLM importance classification per turn, compaction service.

**Files:** `MemoryRetriever.cs` (new), `MemoryEngine.cs` (new), `AetherSoul.cs`, `SqliteMemorySystem.cs`, `ContextAssembler.cs`, + 2 test files

### Phase 3 — Embeddings
ONNX `e5-small-v2` via `Microsoft.ML.OnnxRuntime`. 384-dim embeddings. Hybrid FTS5+vector with graceful fallback.

**Files:** `OnnxEmbeddingProvider.cs` (new), `MemoryRetriever.cs`

### Phase 4 — Multi-Agent
Shared memory namespace, permission grants, cross-agent queries.

**Files:** `Schema.sql`, `SqliteMemorySystem.cs`, `MemoryEngine.cs`

---

## Files Changed (Complete)

| File | Phase | Change |
|------|-------|--------|
| `src/Aether/Data/Schema.sql` | 1 | Add memory_entries, memory_events, memory_fts |
| `src/Aether/Memory/MemoryTypes.cs` | 1 | Add MemoryEntry, MemoryEvent, SearchQuery records |
| `src/Aether/Memory/SqliteMemorySystem.cs` | 1-2 | Rewrite LoadContextAsync, wire FTS5, hybrid retrieval |
| `src/Aether/Memory/FileMemory.cs` | 1 | Strip session stubs, pure CLAUDE.md loader |
| `src/Aether/Agent/ContextAssembler.cs` | 1-2 | Replace 2-day lookback, wire DB context |
| `src/Aether/Agent/AetherSoul.cs` | 2 | LLM importance classify per turn |
| `src/Aether/Memory/MemoryRetriever.cs` | 2 | **New** — MMR, scoring, decay, budget |
| `src/Aether/Memory/MemoryEngine.cs` | 2 | **New** — lifecycle hooks, session init, compaction host |
| `src/Aether/Providers/OnnxEmbeddingProvider.cs` | 3 | **New** — ONNX inference |
| `tests/Aether.Tests/MemoryRetrieverTests.cs` | 2 | **New** |
| `tests/Aether.Tests/MemoryEngineTests.cs` | 2 | **New** |
