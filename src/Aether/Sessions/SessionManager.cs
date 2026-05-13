using Aether.Data;
using Aether.Plugins;

namespace Aether.Sessions;

public class SessionManager
{
    private readonly AetherDb _db;
    private readonly HookEngine? _hooks;
    // In-memory session store for MVP — avoids DB async overhead in agent loop.
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly Dictionary<string, List<SessionMessage>> _messages = new();

    protected SessionManager() { _db = null!; }

    public SessionManager(AetherDb db, HookEngine? hooks = null)
    {
        _db = db;
        _hooks = hooks;
    }

    // ── Sync API (used by agent loop) ──

    public virtual Session GetOrCreateSession(string groupFolder)
    {
        if (_sessions.TryGetValue(groupFolder, out var existing))
            return existing;

        var session = new Session(Guid.NewGuid().ToString("N"), groupFolder,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _sessions[groupFolder] = session;
        return session;
    }

    public virtual void AppendMessage(string sessionId, SessionMessage message)
    {
        if (!_messages.TryGetValue(sessionId, out var list))
            _messages[sessionId] = list = new List<SessionMessage>();
        list.Add(message);
    }

    public virtual IReadOnlyList<SessionMessage> GetHistory(string sessionId, int maxMessages)
    {
        if (!_messages.TryGetValue(sessionId, out var list))
            return Array.Empty<SessionMessage>();

        return list.TakeLast(maxMessages).ToList();
    }

    // ── Async API (backward compat — delegates to DB) ──

    public virtual async Task<Session> CreateSessionAsync(string groupFolder, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var session = new Session(Guid.NewGuid().ToString("N"), groupFolder, now, now);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO sessions (id, group_folder, created_at, last_activity)
            VALUES ($id, $groupFolder, $createdAt, $lastActivity);
            """;
        insert.Parameters.AddWithValue("$id", session.Id);
        insert.Parameters.AddWithValue("$groupFolder", session.GroupFolder);
        insert.Parameters.AddWithValue("$createdAt", session.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("$lastActivity", session.LastActivity.ToString("O"));
        await insert.ExecuteNonQueryAsync(ct);

        await FireSessionStartAsync(session, isNewSession: true, ct);
        return session;
    }

    public virtual async Task<Session> GetOrCreateSessionAsync(string groupFolder, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);

        await using (var find = connection.CreateCommand())
        {
            find.CommandText = """
                SELECT id, group_folder, created_at, last_activity
                FROM sessions
                WHERE group_folder = $groupFolder
                ORDER BY last_activity DESC
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$groupFolder", groupFolder);

            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var existing = new Session(
                    Id: reader.GetString(0),
                    GroupFolder: reader.GetString(1),
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(2)),
                    LastActivity: DateTimeOffset.Parse(reader.GetString(3)));
                await FireSessionStartAsync(existing, isNewSession: false, ct);
                return existing;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var session = new Session(Guid.NewGuid().ToString("N"), groupFolder, now, now);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO sessions (id, group_folder, created_at, last_activity)
            VALUES ($id, $groupFolder, $createdAt, $lastActivity);
            """;
        insert.Parameters.AddWithValue("$id", session.Id);
        insert.Parameters.AddWithValue("$groupFolder", session.GroupFolder);
        insert.Parameters.AddWithValue("$createdAt", session.CreatedAt.ToString("O"));
        insert.Parameters.AddWithValue("$lastActivity", session.LastActivity.ToString("O"));
        await insert.ExecuteNonQueryAsync(ct);

        await FireSessionStartAsync(session, isNewSession: true, ct);
        return session;
    }

    public virtual async Task CompactSessionAsync(
        string sessionId,
        int targetTokens,
        string? summary = null,
        CancellationToken ct = default)
    {
        if (!_messages.TryGetValue(sessionId, out var messages))
            return;

        var tokensBefore = EstimateTokens(messages);

        while (messages.Count > 0 && EstimateTokens(messages) > targetTokens)
            messages.RemoveAt(0);

        if (_hooks is not null)
        {
            var ctx = new OnSessionCompactContext
            {
                SessionId = sessionId,
                TokensBefore = tokensBefore,
                TokensAfter = EstimateTokens(messages),
                Summary = summary
            };
            await _hooks.RunAllAsync(HookPoint.OnSessionCompact, ctx, ct);
        }
    }

    public virtual async Task AppendMessageAsync(string sessionId, SessionMessage message, CancellationToken ct = default)
    {
        // Also write to in-memory store
        AppendMessage(sessionId, message);

        await using var connection = await _db.OpenConnectionAsync(ct);

        string groupFolder;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT group_folder FROM sessions WHERE id = $id LIMIT 1;";
            find.Parameters.AddWithValue("$id", sessionId);
            var result = await find.ExecuteScalarAsync(ct);
            groupFolder = result as string ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO messages (id, group_jid, sender, content, timestamp, is_from_me, is_bot_message, session_id)
                VALUES ($id, $groupFolder, $role, $content, $timestamp, $isFromMe, $isBotMessage, $sessionId);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$groupFolder", groupFolder);
            insert.Parameters.AddWithValue("$role", message.Role);
            insert.Parameters.AddWithValue("$content", message.Content);
            insert.Parameters.AddWithValue("$timestamp", message.Timestamp.ToString("O"));
            insert.Parameters.AddWithValue("$isFromMe", message.Role == "assistant" ? 1 : 0);
            insert.Parameters.AddWithValue("$isBotMessage", message.Role == "assistant" ? 1 : 0);
            insert.Parameters.AddWithValue("$sessionId", sessionId);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE sessions SET last_activity = $lastActivity WHERE id = $id;";
        update.Parameters.AddWithValue("$lastActivity", message.Timestamp.ToString("O"));
        update.Parameters.AddWithValue("$id", sessionId);
        await update.ExecuteNonQueryAsync(ct);
    }

    public virtual async Task<IReadOnlyList<SessionMessage>> GetHistoryAsync(string sessionId, int maxMessages, CancellationToken ct = default)
    {
        // Try in-memory first
        var memHistory = GetHistory(sessionId, maxMessages);
        if (memHistory.Count > 0) return memHistory;

        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sender, content, timestamp
            FROM messages
            WHERE session_id = $sessionId
            ORDER BY timestamp ASC
            LIMIT $maxMessages;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$maxMessages", maxMessages);

        var messages = new List<SessionMessage>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(new SessionMessage(
                Role: reader.GetString(0),
                Content: reader.GetString(1),
                Timestamp: DateTimeOffset.Parse(reader.GetString(2))));
        }

        return messages;
    }

    public virtual async Task<IReadOnlyList<Session>> GetRecentSessionsAsync(int limit = 10, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, group_folder, created_at, last_activity
            FROM sessions
            ORDER BY last_activity DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var sessions = new List<Session>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            sessions.Add(new Session(
                Id: reader.GetString(0),
                GroupFolder: reader.GetString(1),
                CreatedAt: DateTimeOffset.Parse(reader.GetString(2)),
                LastActivity: DateTimeOffset.Parse(reader.GetString(3))));
        }

        return sessions;
    }

    private async Task FireSessionStartAsync(Session session, bool isNewSession, CancellationToken ct)
    {
        if (_hooks is null) return;

        var ctx = new OnSessionStartContext
        {
            AgentName = session.GroupFolder,
            WorkspacePath = session.GroupFolder,
            SessionId = session.Id,
            IsNewSession = isNewSession
        };
        await _hooks.RunAllAsync(HookPoint.OnSessionStart, ctx, ct);
    }

    private static int EstimateTokens(IEnumerable<SessionMessage> messages)
        => Math.Max(1, messages.Sum(m => m.Content.Length) / 4);
}
