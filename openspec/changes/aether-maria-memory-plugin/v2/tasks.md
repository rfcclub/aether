# Task: MariaMemoryPlugin Phase 2 (The 7 Gems)

Implement the additive enhancements suggested by Maria to transition the memory system from a frame to a living organism.

## 1. Multi-Layer Storage (SQLite + JSONL)
- [x] Implement `MariaSqliteStore` class for `store/maria_memory.db`.
- [x] Create `memory_nodes` table with FTS5 and `edges` table for graph links.
- [x] Update `MariaMemoryStore` to write to both JSONL and SQLite (Dual-Write).
- [x] Migration: Populate SQLite from existing JSONL if any.

## 2. Intelligence Engines
- [x] Implement `AutoPromotionEngine` with scoring logic (Significance, Recall, Age, User, Graph).
- [x] Implement `ContextAssemblyEngine` with the `recency * relevance * importance` algorithm.
- [x] Add `thread_id` to `MemoryNode` and update auto-save logic to track threads.

## 3. Dreaming & Research
- [x] Implement `DreamingService` (periodic background task).
- [x] Add basic "Light Dreaming" (keyword/tagging) and "REM Dreaming" (pattern detection).
- [x] Implement `ResearchLinker` to connect `research_findings.md` to memory nodes via SQLite edges.

## 4. Interfaces & APIs
- [x] Create `Aether.Plugins.MariaMemory.Api` (minimal REST controller using Aether's web surface).
- [x] Implement `/context`, `/query`, and `/2b/status` endpoints.
- [x] Implement `LegacyPythonBridge` to execute old `mariamem_*` tools using `bridge.py`.

## 5. Configuration & Commands
- [x] Update `config.json` with new flags (auto_promotion, tokens_limit, dreaming_interval).
- [x] Add slash commands: `/memory link-research`, `/memory promote`, `/2b status`.

## 6. Verification
- [x] Verify SQLite index matches JSONL count.
- [x] Verify context assembly respects token budget (7000 tokens).
- [x] Verify background dreaming task logs a pulse every interval.
