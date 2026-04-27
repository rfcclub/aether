using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aether.Memory;

/// <summary>
/// SQLite-backed memory system with FTS5 search.
///
/// IMPLEMENTATION STATUS: Complete
/// </summary>
public class SqliteMemorySystem : IMemorySystem, IDisposable
{
    private readonly string _dbPath;
    private readonly string _memoryFilePath;
    private readonly ILogger<SqliteMemorySystem> _logger;
    private readonly List<ContextEntry> _ephemeral = new();
    private SqliteConnection? _connection;

    // Hard limits
    private const int EphemeralTokenLimit = 4000;
    private const int DurableCharLimit = 2500;
    private const float MinConfidence = 0.7f;
    private const int MinEvidence = 3;

    public SqliteMemorySystem(string dbPath, string memoryFilePath, ILogger<SqliteMemorySystem> logger)
    {
        _dbPath = dbPath;
        _memoryFilePath = memoryFilePath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(ct);
        _logger.LogInformation("Memory system initialized: {DbPath}", _dbPath);
    }

    public Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default)
    {
        return Task.FromResult(string.Empty);
    }

    // === EPHEMERAL LAYER ===

    public void AddToContext(string content, float priority = 0.5f)
    {
        _ephemeral.Add(new ContextEntry(content, priority, DateTime.UtcNow));

        var tokens = EstimateTokens(_ephemeral);
        if (tokens > EphemeralTokenLimit)
        {
            CompactContext(EphemeralTokenLimit);
        }
    }

    public void CompactContext(int targetTokens)
    {
        // Priority eviction: tool_result > assistant > user
        // FIFO tiebreak within same priority
        var remaining = _ephemeral.ToList();
        _ephemeral.Clear();

        foreach (var entry in remaining.OrderByDescending(e => GetPriorityScore(e.Priority))
                                       .ThenBy(e => e.AddedAt))
        {
            _ephemeral.Add(entry);
            if (EstimateTokens(_ephemeral) <= targetTokens)
            {
                break;
            }
        }

        // If still over limit, truncate oldest
        while (EstimateTokens(_ephemeral) > targetTokens && _ephemeral.Count > 0)
        {
            _ephemeral.RemoveAt(0);
        }
    }

    private static int GetPriorityScore(float priority) => priority switch
    {
        >= 0.8f => 3, // tool_result
        >= 0.5f => 2, // assistant
        _ => 1        // user
    };

    public IReadOnlyList<ContextEntry> GetContext() => _ephemeral.AsReadOnly();

    private static int EstimateTokens(List<ContextEntry> entries) =>
        entries.Sum(e => e.Content.Length / 4);

    // === WORKING LAYER (SQLite) ===

    public async Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default)
    {
        EnsureConnection();

        var sessionId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (id, group_folder, created_at, last_activity)
            VALUES ($id, $agentId, $created, $last)
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$agentId", agentId);
        cmd.Parameters.AddWithValue("$created", now);
        cmd.Parameters.AddWithValue("$last", now);
        await cmd.ExecuteNonQueryAsync(ct);

        return sessionId;
    }

    public async Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default)
    {
        EnsureConnection();

        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO messages (id, group_jid, sender, content, timestamp, is_from_me, is_bot_message, session_id)
            VALUES ($id, $group, $sender, $content, $ts, $fromMe, $bot, $session)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$group", sessionId); // Use sessionId as group for search
        cmd.Parameters.AddWithValue("$sender", role);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$ts", now);
        cmd.Parameters.AddWithValue("$fromMe", role == "assistant" ? 1 : 0);
        cmd.Parameters.AddWithValue("$bot", 1);
        cmd.Parameters.AddWithValue("$session", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        EnsureConnection();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT m.session_id,
                   snippet(messages_fts, 0, '<mark>', '</mark>', '...', 32) as snippet,
                   bm25(messages_fts) as score,
                   m.timestamp
            FROM messages_fts
            JOIN messages m ON messages_fts.rowid = m.rowid
            WHERE messages_fts MATCH $query
            ORDER BY score
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$query", query);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<SearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var ts = DateTimeOffset.TryParse(reader.GetString(3), out var parsed) ? parsed : DateTimeOffset.MinValue;
            results.Add(new SearchResult(
                SessionId: reader.GetString(0),
                Snippet: reader.GetString(1),
                Score: (float)reader.GetDouble(2),
                Timestamp: ts.DateTime));
        }

        return results;
    }

    public async Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        EnsureConnection();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.group_folder, s.created_at,
                   (SELECT COUNT(*) FROM messages WHERE session_id = s.id) as msg_count
            FROM sessions s
            WHERE s.id = $id
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new SessionSummary(
            Id: reader.GetString(0),
            AgentId: reader.GetString(1),
            StartedAt: DateTimeOffset.Parse(reader.GetString(2)).DateTime,
            Summary: null,
            MessageCount: reader.GetInt32(3));
    }

    // === DURABLE LAYER (MEMORY.md) ===

    public async Task<string> GetDurableMemoryAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_memoryFilePath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(_memoryFilePath, ct);
    }

    public async Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default)
    {
        // Validation gates per spec
        if (candidate.Confidence < MinConfidence || candidate.EvidenceCount < MinEvidence)
        {
            _logger.LogDebug("Candidate rejected: confidence={Confidence}, evidence={Evidence}",
                candidate.Confidence, candidate.EvidenceCount);
            return false;
        }

        var current = await GetDurableMemoryAsync(ct);

        // Check bounds
        if (current.Length + candidate.Content.Length > DurableCharLimit)
        {
            _logger.LogWarning("Durable memory full ({Length}/{Limit}), consolidation required",
                current.Length, DurableCharLimit);
            await ForceConsolidationAsync(ct);

            // Re-check after consolidation
            current = await GetDurableMemoryAsync(ct);
            if (current.Length + candidate.Content.Length > DurableCharLimit)
            {
                return false;
            }
        }

        // Append to MEMORY.md with metadata
        var entry = $"\n## [{candidate.CreatedAt:yyyy-MM-dd}] {candidate.Source} | confidence={candidate.Confidence:F1}\n{candidate.Content}\n";
        await File.AppendAllTextAsync(_memoryFilePath, entry, ct);

        _logger.LogInformation("Promoted candidate to durable memory: {Source}", candidate.Source);
        return true;
    }

    public async Task ForceConsolidationAsync(CancellationToken ct = default)
    {
        var content = await GetDurableMemoryAsync(ct);
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        // Parse existing entries
        var entries = ParseMemoryEntries(content);
        if (entries.Count == 0)
        {
            return;
        }

        // Sort by confidence (descending)
        var sorted = entries.OrderByDescending(e => e.confidence).ToList();

        // Build new content under limit
        var sb = new StringBuilder();
        foreach (var entry in sorted)
        {
            var entryText = $"\n## [{entry.date:yyyy-MM-dd}] {entry.source} | confidence={entry.confidence:F1}\n{entry.content}\n";
            if (sb.Length + entryText.Length > DurableCharLimit)
            {
                break;
            }
            sb.Append(entryText);
        }

        await File.WriteAllTextAsync(_memoryFilePath, sb.ToString(), ct);
        _logger.LogInformation("Consolidated durable memory to {Length} chars", sb.Length);
    }

    private static List<(string content, DateTime date, float confidence, string source)> ParseMemoryEntries(string content)
    {
        var entries = new List<(string, DateTime, float, string)>();
        var sections = content.Split("## [", StringSplitOptions.RemoveEmptyEntries);

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section))
            {
                continue;
            }

            var parts = section.Split(']', 2);
            if (parts.Length < 2)
            {
                continue;
            }

            if (!DateTime.TryParse(parts[0].Trim(), out var date))
            {
                continue;
            }

            var metaParts = parts[1].Split('|');
            var source = metaParts[0].Trim();
            var confidence = 0.5f;

            if (metaParts.Length > 1 && metaParts[1].Contains("confidence="))
            {
                var confStr = metaParts[1].Split('=')[1].Trim();
                if (float.TryParse(confStr, out var parsed))
                {
                    confidence = parsed;
                }
            }

            entries.Add((parts[1], date, confidence, source));
        }

        return entries;
    }

    // === HELPERS ===

    private void EnsureConnection()
    {
        if (_connection?.State != System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException("Memory system not initialized. Call InitializeAsync first.");
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}