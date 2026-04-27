using Aether.Data;

namespace Aether.Sessions;

public sealed class SessionManager : ISessionManager
{
    private readonly AetherDb _db;

    public SessionManager(AetherDb db)
    {
        _db = db;
    }

    public async Task<Session> GetOrCreateSessionAsync(string groupFolder, CancellationToken ct = default)
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
                return new Session(
                    Id: reader.GetString(0),
                    GroupFolder: reader.GetString(1),
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(2)),
                    LastActivity: DateTimeOffset.Parse(reader.GetString(3)));
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

        return session;
    }

    public async Task AppendMessageAsync(string sessionId, SessionMessage message, CancellationToken ct = default)
    {
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

    public async Task<IReadOnlyList<SessionMessage>> GetHistoryAsync(string sessionId, int maxMessages, CancellationToken ct = default)
    {
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
}

public interface ISessionManager
{
    Task<Session> GetOrCreateSessionAsync(string groupFolder, CancellationToken ct = default);
    Task AppendMessageAsync(string sessionId, SessionMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<SessionMessage>> GetHistoryAsync(string sessionId, int maxMessages, CancellationToken ct = default);
}
