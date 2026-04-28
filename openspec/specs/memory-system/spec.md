# memory-system Specification

## Purpose
TBD - created by archiving change aether-full-project. Update Purpose after archive.
## Requirements
### Requirement: SQLite schema initialized at host startup
The memory system SHALL call `InitializeAsync()` during `IHostedService.StartAsync()`, creating the sessions table, messages table, FTS5 virtual table, and promotion_candidates table if they do not exist.

#### Scenario: First-time startup
- **WHEN** `aether.db` does not exist and the host starts
- **THEN** the system SHALL create the database, execute `Schema.sql`, and log "Memory schema initialized"

#### Scenario: Schema already exists
- **WHEN** `aether.db` already has the expected tables
- **THEN** `InitializeAsync()` SHALL be idempotent (using `CREATE TABLE IF NOT EXISTS` and `CREATE VIRTUAL TABLE IF NOT EXISTS`)

### Requirement: FTS5 full-text search with BM25 ranking
`SearchAsync(groupFolder, query, topK)` SHALL use the FTS5 `messages_fts` virtual table with native `bm25()` ranking to return the top-K most relevant messages.

#### Scenario: Search returns ranked results
- **WHEN** `SearchAsync("chat-123", "deployment failure", 5)` is called
- **THEN** the system SHALL return up to 5 messages ranked by BM25 relevance, most relevant first

#### Scenario: No matching messages
- **WHEN** the query matches no messages in the group
- **THEN** `SearchAsync` SHALL return an empty list without error

### Requirement: Ephemeral buffer uses priority eviction
The ephemeral message buffer SHALL evict the lowest-priority messages when the token limit (~4,000 tokens) is exceeded, not the oldest messages (FIFO).

#### Scenario: Buffer overflow triggers priority eviction
- **WHEN** adding a message would exceed the 4,000-token ephemeral limit
- **THEN** the system SHALL evict the message(s) with the lowest importance score (`tool_result > assistant > user`) until space is available

#### Scenario: Equal priority — FIFO tiebreak
- **WHEN** multiple messages share the lowest priority level
- **THEN** the oldest message among them SHALL be evicted first

### Requirement: Promotion pipeline writes to MEMORY.md
`TryPromoteAsync` SHALL promote a `PromotionCandidate` with `confidence ≥ 0.7` and `evidence_count ≥ 3` to `MEMORY.md` when the durable char limit (2,500) is not exceeded.

#### Scenario: Successful promotion
- **WHEN** candidate meets thresholds and MEMORY.md is under limit
- **THEN** the candidate text SHALL be appended to MEMORY.md and the candidate marked `status = APPLIED` in the database

#### Scenario: MEMORY.md at capacity — consolidation triggered
- **WHEN** promotion would exceed 2,500 chars
- **THEN** `ForceConsolidationAsync` SHALL merge or evict the lowest-confidence existing entries before appending

### Requirement: FTS5 sync trigger on message insert
The database schema SHALL include an `AFTER INSERT ON messages` trigger that populates `messages_fts` automatically.

#### Scenario: Message insert syncs FTS5
- **WHEN** a row is inserted into the `messages` table
- **THEN** the trigger SHALL insert `(rowid, content)` into `messages_fts` in the same transaction

