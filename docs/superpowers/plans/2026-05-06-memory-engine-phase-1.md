# Memory Engine Phase 1 — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all known memory gaps — `LoadContextAsync` stub, FTS5 never queried, 2-day hard lookback, no `memory_entries` table.

**Architecture:** Add `memory_entries` + `memory_events` + `memory_fts` tables to Schema.sql. Rewrite `LoadContextAsync` with real FTS5 search. Replace `ContextAssembler.MemoryLookbackDays=2` with DB-driven retrieval. Strip `FileMemory` session stubs, keeping only CLAUDE.md loading.

**Tech Stack:** .NET 9, SQLite (FTS5 exists), Microsoft.Data.Sqlite, xUnit

---

### Task 1: Add memory tables to Schema.sql

**Files:**
- Modify: `src/Aether/Data/Schema.sql`

- [ ] **Step 1: Add memory_entries, memory_events, memory_fts tables**

Add after the `pipeline_states` table (end of file):

```sql

-- Memory engine tables
CREATE TABLE IF NOT EXISTS memory_entries (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL,
    tier TEXT NOT NULL DEFAULT 'working',
    content TEXT NOT NULL,
    embedding BLOB,
    importance REAL NOT NULL DEFAULT 0.5,
    access_count INTEGER NOT NULL DEFAULT 0,
    source_session TEXT,
    created_at TEXT NOT NULL,
    last_accessed TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS memory_events (
    id TEXT PRIMARY KEY,
    agent_id TEXT NOT NULL,
    event_type TEXT NOT NULL,
    entry_id TEXT,
    summary TEXT NOT NULL,
    token_count INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL
);

-- FTS5 on memory content (separate from messages_fts)
CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
    content,
    content=memory_entries,
    content_rowid=rowid
);

-- Triggers to keep memory_fts in sync
CREATE TRIGGER IF NOT EXISTS memory_entries_ai AFTER INSERT ON memory_entries BEGIN
    INSERT INTO memory_fts(rowid, content) VALUES (new.rowid, new.content);
END;

CREATE TRIGGER IF NOT EXISTS memory_entries_ad AFTER DELETE ON memory_entries BEGIN
    INSERT INTO memory_fts(memory_fts, rowid, content) VALUES('delete', old.rowid, old.content);
END;

CREATE TRIGGER IF NOT EXISTS memory_entries_au AFTER UPDATE ON memory_entries BEGIN
    INSERT INTO memory_fts(memory_fts, rowid, content) VALUES('delete', old.rowid, old.content);
    INSERT INTO memory_fts(rowid, content) VALUES (new.rowid, new.content);
END;
```

- [ ] **Step 2: Verify schema parses correctly**

Run: `sqlite3 /tmp/test-memory-schema.db < src/Aether/Data/Schema.sql`
Expected: no errors, exit 0. Then: `rm /tmp/test-memory-schema.db`

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Data/Schema.sql
git commit -m "feat: add memory_entries, memory_events, memory_fts tables"
```

---

### Task 2: Add memory types

**Files:**
- Modify: `src/Aether/Memory/MemoryTypes.cs`

- [ ] **Step 1: Read current MemoryTypes.cs**

Read the file to see existing types (`ContextEntry`, `SearchResult`, `SessionSummary`, `PromotionCandidate`).

- [ ] **Step 2: Add new records**

Append these records to the file (keep all existing types):

```csharp
public record MemoryEntry(
    string Id,
    string AgentId,
    string Tier,
    string Content,
    float Importance,
    int AccessCount,
    string? SourceSession,
    DateTime CreatedAt,
    DateTime LastAccessed
);

public enum MemoryEventType { Insert, Promote, Compact, Reflect }

public record MemoryEvent(
    string Id,
    string AgentId,
    MemoryEventType EventType,
    string? EntryId,
    string Summary,
    int TokenCount,
    DateTime CreatedAt
);

public record SearchQuery(
    string Query,
    string AgentId,
    int Limit = 10,
    bool IncludeArchival = false
);
```

- [ ] **Step 3: Build to verify types compile**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q 2>&1 | tail -5`
Expected: 0 Error(s)

- [ ] **Step 4: Commit**

```bash
git add src/Aether/Memory/MemoryTypes.cs
git commit -m "feat: add MemoryEntry, MemoryEvent, SearchQuery records"
```

---

### Task 3: Rewrite SqliteMemorySystem.LoadContextAsync

**Files:**
- Modify: `src/Aether/Memory/SqliteMemorySystem.cs:46-49`

- [ ] **Step 1: Read the test file for existing memory tests**

Read `tests/Aether.Tests/` for any existing SqliteMemorySystem tests to understand patterns.

- [ ] **Step 2: Write the failing test**

Create `tests/Aether.Tests/MemorySystemTests.cs`:

```csharp
using Aether.Memory;
using Xunit;

namespace Aether.Tests;

public sealed class MemorySystemTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private SqliteMemorySystem _system = null!;

    public MemorySystemTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether_test_{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        _system = new SqliteMemorySystem(_dbPath, Path.GetTempPath() + "MEMORY.md", 
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteMemorySystem>.Instance);
        await _system.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        _system.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LoadContextAsync_ReturnsEntries_MatchingQuery()
    {
        // Insert a memory entry
        var entry = new MemoryEntry(
            Id: "test-1", AgentId: "maria", Tier: "working",
            Content: "User prefers dark themes for all applications",
            Importance: 0.9f, AccessCount: 1,
            SourceSession: "session-1",
            CreatedAt: DateTime.UtcNow, LastAccessed: DateTime.UtcNow);
        await _system.InsertMemoryEntryAsync(entry);

        // Search for it
        var context = await _system.LoadContextAsync("maria", "dark theme preferences");

        Assert.Contains("dark themes", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadContextAsync_ReturnsEmpty_WhenNoMatch()
    {
        var context = await _system.LoadContextAsync("maria", "nonexistent query xyz123");
        Assert.Equal(string.Empty, context);
    }

    [Fact]
    public async Task InsertMemoryEntry_PersistsToDb()
    {
        var entry = new MemoryEntry(
            Id: "test-2", AgentId: "maria", Tier: "durable",
            Content: "Project uses .NET 9 with SQLite",
            Importance: 0.8f, AccessCount: 0,
            SourceSession: null,
            CreatedAt: DateTime.UtcNow, LastAccessed: DateTime.UtcNow);
        await _system.InsertMemoryEntryAsync(entry);

        var context = await _system.LoadContextAsync("maria", ".NET 9");
        Assert.Contains(".NET 9", context, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --filter "FullyQualifiedName~MemorySystemTests" -v n`
Expected: FAIL — `InsertMemoryEntryAsync` not defined, `LoadContextAsync` returns empty string

- [ ] **Step 4: Rewrite LoadContextAsync and add InsertMemoryEntryAsync**

Replace `LoadContextAsync` stub at `SqliteMemorySystem.cs:46-49`:

```csharp
public async Task<string> LoadContextAsync(string agentId, string query, int limit = 5, CancellationToken ct = default)
{
    EnsureConnection();

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = """
        SELECT me.content,
               bm25(memory_fts) as score,
               me.importance,
               me.created_at
        FROM memory_fts
        JOIN memory_entries me ON memory_fts.rowid = me.rowid
        WHERE memory_fts MATCH $query AND me.agent_id = $agentId AND me.tier != 'archival'
        ORDER BY score
        LIMIT $limit
        """;
    cmd.Parameters.AddWithValue("$query", query);
    cmd.Parameters.AddWithValue("$agentId", agentId);
    cmd.Parameters.AddWithValue("$limit", limit);

    var sb = new StringBuilder();
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    var first = true;
    while (await reader.ReadAsync(ct))
    {
        if (!first) sb.AppendLine();
        sb.Append("- ").Append(reader.GetString(0));
        first = false;

        // Bump access_count + last_accessed on read
        var content = reader.GetString(0);
        await using var updateCmd = _connection.CreateCommand();
        updateCmd.CommandText = """
            UPDATE memory_entries SET access_count = access_count + 1, last_accessed = $now
            WHERE content = $content
            """;
        updateCmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        updateCmd.Parameters.AddWithValue("$content", content);
        await updateCmd.ExecuteNonQueryAsync(ct);
    }

    return sb.ToString();
}
```

Add new method `InsertMemoryEntryAsync`:

```csharp
public async Task InsertMemoryEntryAsync(MemoryEntry entry, CancellationToken ct = default)
{
    EnsureConnection();

    await using var cmd = _connection!.CreateCommand();
    cmd.CommandText = """
        INSERT INTO memory_entries (id, agent_id, tier, content, embedding, importance, access_count, source_session, created_at, last_accessed)
        VALUES ($id, $agentId, $tier, $content, $embedding, $importance, $accessCount, $sourceSession, $createdAt, $lastAccessed)
        """;
    cmd.Parameters.AddWithValue("$id", entry.Id);
    cmd.Parameters.AddWithValue("$agentId", entry.AgentId);
    cmd.Parameters.AddWithValue("$tier", entry.Tier);
    cmd.Parameters.AddWithValue("$content", entry.Content);
    cmd.Parameters.AddWithValue("$embedding", entry.Embedding is not null ? (object)entry.Embedding : DBNull.Value);
    cmd.Parameters.AddWithValue("$importance", entry.Importance);
    cmd.Parameters.AddWithValue("$accessCount", entry.AccessCount);
    cmd.Parameters.AddWithValue("$sourceSession", entry.SourceSession is not null ? (object)entry.SourceSession : DBNull.Value);
    cmd.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
    cmd.Parameters.AddWithValue("$lastAccessed", entry.LastAccessed.ToString("O"));
    await cmd.ExecuteNonQueryAsync(ct);
}
```

Also keep the old `LoadContextAsync(string groupFolder, CancellationToken ct)` for backward compat, delegating to the new method with an empty query:

```csharp
public Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default)
    => LoadContextAsync(groupFolder, string.Empty, limit: 5, ct);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --filter "FullyQualifiedName~MemorySystemTests" -v n`
Expected: 3 tests PASS

- [ ] **Step 6: Run full test suite to check for regressions**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q 2>&1 | tail -5`
Expected: Only pre-existing `TavilyWebSearchProviderTests.MissingApiKey_Throws` fails, all others pass

- [ ] **Step 7: Commit**

```bash
git add src/Aether/Memory/SqliteMemorySystem.cs tests/Aether.Tests/MemorySystemTests.cs
git commit -m "feat: implement LoadContextAsync with FTS5 search, add InsertMemoryEntryAsync"
```

---

### Task 4: Strip FileMemory session stubs

**Files:**
- Modify: `src/Aether/Memory/FileMemory.cs`

- [ ] **Step 1: Read current FileMemory.cs**

Read the file to identify which methods are session-related stubs that should be removed. The methods `CreateSessionAsync`, `AppendMessageAsync`, `GetRecentSessionsAsync`, `GetSessionAsync`, `LoadContextAsync`, `SearchAsync`, `TryPromoteAsync`, `GetDurableMemoryAsync`, `ForceConsolidationAsync` that just return empty/Task.CompletedTask are stubs — these are now handled by `SqliteMemorySystem`.

- [ ] **Step 2: Remove session stub methods, keep CLAUDE.md loading**

Remove these methods from `FileMemory.cs`:
- `CreateSessionAsync` (if it's a no-op stub)
- `AppendMessageAsync` (if it's a no-op stub)
- `GetRecentSessionsAsync` (if it's a no-op stub)
- `GetSessionAsync` (if it's a no-op stub)
- `LoadContextAsync` (if it's a no-op stub)
- `SearchAsync` (if it's a no-op stub)
- `TryPromoteAsync` (if it's a no-op stub)
- `GetDurableMemoryAsync` (if it's a stub that reads MEMORY.md — SqliteMemorySystem already handles this)
- `ForceConsolidationAsync` (if it's a no-op stub)
- `Ephemeral` list and related `AddToContext`/`CompactContext`/`GetContext` if they're stubs

Keep:
- `LoadClaudeMdAsync(string groupFolder)` if it exists — reads CLAUDE.md from agent directory
- Constructor and any CLAUDE.md-related config

If `FileMemory` becomes empty after stripping (only constructor + CLAUDE.md loader), rename it or mark as obsolete with a comment: `// Legacy: only used for CLAUDE.md loading`

- [ ] **Step 3: Build to verify no broken references**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q 2>&1 | tail -5`
Expected: 0 Error(s). If compilation errors: check callers of removed methods, update them to use `SqliteMemorySystem` instead.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q 2>&1 | tail -5`
Expected: Only pre-existing Tavily failure, all others pass

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Memory/FileMemory.cs
git commit -m "refactor: strip FileMemory session stubs, keep CLAUDE.md loading"
```

---

### Task 5: Replace 2-day lookback with DB-driven context

**Files:**
- Modify: `src/Aether/Agent/ContextAssembler.cs`
- Modify: `src/Aether/Agents/AgentProfile.cs` (if it calls `LoadDailyMemory`)

- [ ] **Step 1: Read current ContextAssembler.cs AgentProfile.cs memory loading**

Read `ContextAssembler.cs` — find `MemoryLookbackDays = 2` and `IsRecentEnough` method.
Read `AgentProfile.cs` — find `LoadDailyMemory` or any method that feeds `recentMemory` to `AssembleDynamicContext`.

- [ ] **Step 2: Update ContextAssembler to accept DB context**

Change `MemoryLookbackDays` from 2 to 30:

```csharp
private const int MemoryLookbackDays = 30;
```

Add a new parameter to `AssembleDynamicContext` for DB results:

```csharp
public string AssembleDynamicContext(
    string? workingState = null,
    string? recentMemory = null,
    string? groupContext = null,
    string? dbMemoryContext = null)   // NEW — from SqliteMemorySystem.LoadContextAsync
{
    var sb = new StringBuilder();

    if (!string.IsNullOrWhiteSpace(workingState))
    {
        sb.AppendLine("## Working State");
        sb.AppendLine(workingState);
        sb.AppendLine();
    }

    if (!string.IsNullOrWhiteSpace(recentMemory))
    {
        sb.AppendLine("## Recent Memory");
        sb.AppendLine(recentMemory);
        sb.AppendLine();
    }

    if (!string.IsNullOrWhiteSpace(dbMemoryContext))   // NEW
    {
        sb.AppendLine("## Relevant Memory");
        sb.AppendLine(dbMemoryContext);
        sb.AppendLine();
    }

    if (!string.IsNullOrWhiteSpace(groupContext))
    {
        sb.AppendLine("## Group Context");
        sb.AppendLine(groupContext);
        sb.AppendLine();
    }

    var result = sb.ToString();
    if (_dynamicTokenBudget > 0 && EstimateTokens(result) > _dynamicTokenBudget)
        result = TruncateToTokenBudget(result, _dynamicTokenBudget);

    return result;
}
```

- [ ] **Step 3: Wire SqliteMemorySystem into AgentProfile context loading**

In `AgentProfile.cs`, find where `AssembleDynamicContext` is called. If it's not called (method may be unused currently), wire it in the method that builds identity context. Add a new method:

```csharp
public async Task<string> LoadDynamicContextAsync(
    SqliteMemorySystem memory,
    string? query,
    string? workingState = null,
    string? recentMemory = null,
    string? groupContext = null,
    CancellationToken ct = default)
{
    var dbContext = query is not null
        ? await memory.LoadContextAsync(_agentName, query, limit: 5, ct)
        : null;

    return _contextAssembler.AssembleDynamicContext(
        workingState, recentMemory, groupContext, dbContext);
}
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q 2>&1 | tail -5`
Expected: 0 Error(s)

- [ ] **Step 5: Run full test suite**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q 2>&1 | tail -5`
Expected: Only pre-existing Tavily failure

- [ ] **Step 6: Commit**

```bash
git add src/Aether/Agent/ContextAssembler.cs src/Aether/Agents/AgentProfile.cs
git commit -m "feat: replace 2-day lookback with DB-driven memory retrieval"
```

---

### Task 6: Initialize memory tables on startup

**Files:**
- Modify: `src/Aether/Data/AetherDb.cs` (or wherever Schema.sql is executed)

- [ ] **Step 1: Verify schema initialization path**

Read `AetherDb.cs` to find where `Schema.sql` is executed. The new tables are `CREATE TABLE IF NOT EXISTS` — they will be created automatically on next startup if schema execution runs. If schema is only run on first-time init, add a migration or always-run block for the new tables.

- [ ] **Step 2: If needed, add migration**

If `AetherDb.cs` only executes `Schema.sql` once, add:

```csharp
// Always-run: ensure memory engine tables exist (idempotent)
private static async Task EnsureMemoryTablesAsync(SqliteConnection connection, CancellationToken ct)
{
    var memorySchema = """
        CREATE TABLE IF NOT EXISTS memory_entries (
            id TEXT PRIMARY KEY,
            agent_id TEXT NOT NULL,
            tier TEXT NOT NULL DEFAULT 'working',
            content TEXT NOT NULL,
            embedding BLOB,
            importance REAL NOT NULL DEFAULT 0.5,
            access_count INTEGER NOT NULL DEFAULT 0,
            source_session TEXT,
            created_at TEXT NOT NULL,
            last_accessed TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS memory_events (
            id TEXT PRIMARY KEY,
            agent_id TEXT NOT NULL,
            event_type TEXT NOT NULL,
            entry_id TEXT,
            summary TEXT NOT NULL,
            token_count INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(content, content=memory_entries, content_rowid=rowid);
        CREATE TRIGGER IF NOT EXISTS memory_entries_ai AFTER INSERT ON memory_entries BEGIN
            INSERT INTO memory_fts(rowid, content) VALUES (new.rowid, new.content);
        END;
        CREATE TRIGGER IF NOT EXISTS memory_entries_ad AFTER DELETE ON memory_entries BEGIN
            INSERT INTO memory_fts(memory_fts, rowid, content) VALUES('delete', old.rowid, old.content);
        END;
        CREATE TRIGGER IF NOT EXISTS memory_entries_au AFTER UPDATE ON memory_entries BEGIN
            INSERT INTO memory_fts(memory_fts, rowid, content) VALUES('delete', old.rowid, old.content);
            INSERT INTO memory_fts(rowid, content) VALUES (new.rowid, new.content);
        END;
        """;
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = memorySchema;
    await cmd.ExecuteNonQueryAsync(ct);
}
```

Call `await EnsureMemoryTablesAsync(_connection!, ct)` in `InitializeAsync`.

- [ ] **Step 3: Build and verify**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q 2>&1 | tail -5`
Expected: 0 Error(s)

- [ ] **Step 4: Commit**

```bash
git add src/Aether/Data/AetherDb.cs
git commit -m "feat: ensure memory engine tables initialized on startup"
```

---

### Verification Checklist

- [ ] `LoadContextAsync` returns FTS5 search results, not empty string
- [ ] `InsertMemoryEntryAsync` persists entries to DB
- [ ] `ContextAssembler.MemoryLookbackDays` changed to 30
- [ ] `AssembleDynamicContext` accepts `dbMemoryContext` from SQLite
- [ ] `FileMemory` session stubs removed
- [ ] Full test suite passes (only pre-existing Tavily failure)
- [ ] All 6 tasks committed with conventional commit messages
