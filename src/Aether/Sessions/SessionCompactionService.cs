using System.Collections.Concurrent;
using System.Text;
using Aether.Data;
using Aether.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Sessions;

public sealed class SessionCompactionService : BackgroundService
{
    private readonly ConcurrentQueue<string> _compactionQueue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SessionManager _sessionManager;
    private readonly ProviderRouter _providerRouter;
    private readonly AetherDb _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SessionCompactionService> _logger;

    public SessionCompactionService(
        SessionManager sessionManager,
        ProviderRouter providerRouter,
        AetherDb db,
        IConfiguration configuration,
        ILogger<SessionCompactionService> logger)
    {
        _sessionManager = sessionManager;
        _providerRouter = providerRouter;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public void EnqueueCompaction(string sessionId)
    {
        _compactionQueue.Enqueue(sessionId);
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_compactionQueue.TryDequeue(out var sessionId))
            {
                try
                {
                    await CompactSessionAsync(sessionId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to compact session {SessionId}", sessionId);
                }
            }
        }
    }

    private sealed record CompactionMessage(string Id, string Role, string Content, DateTimeOffset Timestamp);

    private async Task CompactSessionAsync(string sessionId, CancellationToken ct)
    {
        var triggerThreshold = 20;
        if (int.TryParse(_configuration["compaction:trigger_threshold"], out var parsedTrigger))
        {
            triggerThreshold = parsedTrigger;
        }

        var keepRecentCount = 10;
        if (int.TryParse(_configuration["compaction:keep_recent_count"], out var parsedKeep))
        {
            keepRecentCount = parsedKeep;
        }

        // 1. Fetch full history (bypassing token budget limits to get raw DB state)
        var rawMessages = await GetRawHistoryAsync(sessionId, ct);

        if (rawMessages.Count <= triggerThreshold)
        {
            // Too few messages to warrant an expensive compaction cycle.
            return;
        }

        // 2. Identify messages to summarize (keep the keepRecentCount most recent intact)
        var messagesToSummarize = rawMessages.Take(Math.Max(0, rawMessages.Count - keepRecentCount)).ToList();

        if (messagesToSummarize.Count == 0)
        {
            return;
        }
        
        var contentToSummarize = new StringBuilder();
        foreach (var msg in messagesToSummarize)
        {
            contentToSummarize.AppendLine($"[{msg.Role.ToUpperInvariant()}] {msg.Content}");
        }

        // 3. Ask LLM to summarize
        var prompt = "Summarize the following conversation history. Preserve key facts, decisions, the emotional state of the agent, and any resolved 'tensions'. Be extremely concise. The summary should read as a system status update.\n\n" + contentToSummarize.ToString();
        
        var request = new LlmRequest(new List<LlmMessage>
        {
            LlmMessage.System("You are an automated memory compaction system. Your job is to extract the core state of the conversation."),
            LlmMessage.User(prompt)
        });

        var response = await _providerRouter.CompleteAsync(request, ct);
        var summaryText = $"[System Summary] {response.Content}";

        // 4. Update Database: Delete summarized messages, insert summary
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var msg in messagesToSummarize)
            {
                await using var deleteCmd = connection.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM messages WHERE id = $id";
                deleteCmd.Parameters.AddWithValue("$id", msg.Id);
                await deleteCmd.ExecuteNonQueryAsync(ct);
            }

            await using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = "INSERT INTO messages (id, group_jid, sender, content, timestamp, session_id) VALUES ($id, $group, $sender, $content, $timestamp, $sessionId)";
            insertCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insertCmd.Parameters.AddWithValue("$group", "compaction");
            insertCmd.Parameters.AddWithValue("$sender", "system");
            insertCmd.Parameters.AddWithValue("$content", summaryText);
            insertCmd.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O")); // Fake timestamp slightly in the past
            insertCmd.Parameters.AddWithValue("$sessionId", sessionId);
            await insertCmd.ExecuteNonQueryAsync(ct);

            transaction.Commit();

            // Synchronously rebuild in-memory cache with the compacted state.
            // This eliminates any timing race where GetHistoryAsync is called before
            // the DB state would be re-read (since GetHistoryAsync prefers in-memory).
            var summaryTimestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
            var compactedInMemory = new List<SessionMessage>
            {
                new SessionMessage("system", summaryText, summaryTimestamp)
            };
            compactedInMemory.AddRange(
                rawMessages
                    .Skip(messagesToSummarize.Count)
                    .Select(m => new SessionMessage(m.Role, m.Content, m.Timestamp)));
            _sessionManager.ReplaceHistory(sessionId, compactedInMemory);

            _logger.LogInformation("Compacted session {SessionId}. Summarized {Count} messages into 1.", sessionId, messagesToSummarize.Count);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Database transaction failed during compaction for {SessionId}", sessionId);
            throw;
        }
    }

    private async Task<IReadOnlyList<CompactionMessage>> GetRawHistoryAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, sender, content, timestamp
            FROM messages
            WHERE session_id = $sessionId
            ORDER BY timestamp ASC
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);

        var messages = new List<CompactionMessage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(new CompactionMessage(
                Id: reader.GetString(0),
                Role: reader.GetString(1),
                Content: reader.GetString(2),
                Timestamp: DateTimeOffset.Parse(reader.GetString(3))));
        }
        return messages;
    }
}