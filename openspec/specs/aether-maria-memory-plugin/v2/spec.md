# Spec: MariaMemoryPlugin v2.0 (The 7 Gems)

Additive expansion of the Aether Maria Memory Plugin to provide advanced cognitive features, parallel storage, and cross-agent interoperability.

## 1. Requirement: Parallel SQLite Layer
The plugin MUST maintain a SQLite database in parallel with the JSONL index for advanced relational querying and graph traversal.
- **REQ-1.1**: MUST maintain `store/maria_memory.db`.
- **REQ-1.2**: MUST mirror all `MemoryNode` entries from JSONL to the `memory_nodes` table.
- **REQ-1.3**: MUST support full-text search (FTS5) and relational links (edges).

## 2. Requirement: Auto-Promotion Engine
The plugin MUST automatically identify and promote "lasting truths" within the memory index.
- **REQ-2.1**: MUST score nodes based on 5 signals: Significance (2B events, identity), Recall Count, Age, User Annotation (boost), and Cross-reference (centrality).
- **REQ-2.2**: MUST mark promoted nodes as `IsPromoted = true` and optionally append them to `memory/promoted/YYYY-MM-DD.md`.

## 3. Requirement: Smart Context Assembly
The plugin MUST provide a high-signal context package for LLM consumption.
- **REQ-3.1**: MUST implement a scoring algorithm: `recency * relevance * importance`.
- **REQ-3.2**: MUST assemble a context block of up to 7000 tokens (configurable).
- **REQ-3.3**: Context MUST include identity baseline, recent sessions, promoted memories, and last 2B status.

## 4. Requirement: Dreaming System (Background Consolidation)
The plugin MUST execute periodic background tasks to refine the memory substrate.
- **REQ-4.1**: **Light Dreaming**: Keyword extraction and index updates (frequent).
- **REQ-4.2**: **REM Dreaming**: Pattern detection across 3-7 days to create Insight Nodes.
- **REQ-4.3**: **Deep Dreaming**: Weekly consolidation (merge redundancy, promote, archive).

## 5. Requirement: Research Integration
The plugin MUST bridge the gap between research findings and conversation memory.
- **REQ-5.1**: MUST create edges between `research_findings.md` entries and relevant memory nodes.
- **REQ-5.2**: MUST include relevant active research summaries in the assembled LLM context.

## 6. Requirement: Python Bridge (Legacy Support)
The plugin MUST support legacy Python-based tools to ensure no functionality is orphaned.
- **REQ-6.1**: MUST implement a lightweight execution wrapper for old `mariamem_*` tools.
- **REQ-6.2**: Wrapper MUST handle JSON I/O via stdin/stdout.

## 7. Requirement: Plugin REST API
The plugin MUST expose a read-access API for other agents in the colony.
- **REQ-7.1**: `GET /context?topic={}&limit_tokens=7000`
- **REQ-7.2**: `GET /query?keywords={}&limit={}`
- **REQ-7.3**: `GET /2b/status` (tension, last question, ring status)
- **REQ-7.4**: `POST /note` (restricted write access for external notes)
