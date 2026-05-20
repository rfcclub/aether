using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aether.Data;

public sealed record Goal(
    string Id,
    string AgentId,
    string Title,
    string? Description,
    int Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? Deadline = null,
    DateTimeOffset? CompletedAt = null);

public sealed class GoalStore
{
    private readonly AetherDb _db;

    public GoalStore(AetherDb db)
    {
        _db = db;
    }

    public async Task CreateGoalAsync(Goal goal, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO goals (id, agent_id, title, description, priority, status, created_at, deadline, completed_at)
            VALUES ($id, $agentId, $title, $description, $priority, $status, $created, $deadline, $completed)
            """;
        command.Parameters.AddWithValue("$id", goal.Id);
        command.Parameters.AddWithValue("$agentId", goal.AgentId);
        command.Parameters.AddWithValue("$title", goal.Title);
        command.Parameters.AddWithValue("$description", (object?)goal.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$priority", goal.Priority);
        command.Parameters.AddWithValue("$status", goal.Status);
        command.Parameters.AddWithValue("$created", goal.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deadline", goal.Deadline?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completed", goal.CompletedAt?.ToString("O") ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<Goal>> GetActiveGoalsAsync(string agentId, CancellationToken ct = default)
    {
        var results = new List<Goal>();
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, agent_id, title, description, priority, status, created_at, deadline, completed_at
            FROM goals
            WHERE agent_id = $agentId AND status = 'ACTIVE'
            ORDER BY priority DESC, created_at ASC
            """;
        command.Parameters.AddWithValue("$agentId", agentId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapReaderToGoal(reader));
        }
        return results;
    }

    public async Task UpdateGoalStatusAsync(string id, string status, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        var completedAt = status == "COMPLETED" ? DateTimeOffset.UtcNow.ToString("O") : null;
        
        command.CommandText = "UPDATE goals SET status = $status, completed_at = $completedAt WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completedAt", (object?)completedAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static Goal MapReaderToGoal(SqliteDataReader reader)
    {
        return new Goal(
            Id: reader.GetString(0),
            AgentId: reader.GetString(1),
            Title: reader.GetString(2),
            Description: reader.IsDBNull(3) ? null : reader.GetString(3),
            Priority: reader.GetInt32(4),
            Status: reader.GetString(5),
            CreatedAt: DateTimeOffset.Parse(reader.GetString(6)),
            Deadline: reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
            CompletedAt: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8))
        );
    }
}
