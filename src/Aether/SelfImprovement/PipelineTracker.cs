using System.Security.Cryptography;
using System.Text;
using Aether.Data;
using Aether.Memory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aether.SelfImprovement;

public class PipelineTracker 
{
    private readonly AetherDb _db;
    private readonly ILogger<PipelineTracker> _logger;

    public PipelineTracker(AetherDb db, ILogger<PipelineTracker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public virtual async Task TrackAsync(PromotionCandidate candidate, CancellationToken ct = default)
    {
        var hash = HashContent(candidate.Content);
        var now = DateTime.UtcNow.ToString("O");

        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO pipeline_states (id, candidate_hash, state, source, content, created_at, updated_at)
            VALUES ($id, $hash, $state, $source, $content, $created, $updated)
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$state", CandidateState.PROPOSED.ToString());
        cmd.Parameters.AddWithValue("$source", candidate.Source);
        cmd.Parameters.AddWithValue("$content", candidate.Content);
        cmd.Parameters.AddWithValue("$created", now);
        cmd.Parameters.AddWithValue("$updated", now);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogInformation("Tracked candidate {Hash} as PROPOSED", hash);
    }

    public virtual async Task TransitionAsync(PromotionCandidate candidate, CandidateState newState, CancellationToken ct = default)
    {
        var hash = HashContent(candidate.Content);
        var now = DateTime.UtcNow.ToString("O");

        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE pipeline_states
            SET state = $newState, updated_at = $updated
            WHERE candidate_hash = $hash
            """;
        cmd.Parameters.AddWithValue("$newState", newState.ToString());
        cmd.Parameters.AddWithValue("$updated", now);
        cmd.Parameters.AddWithValue("$hash", hash);

        var affected = await cmd.ExecuteNonQueryAsync(ct);

        if (affected > 0)
        {
            _logger.LogInformation("Candidate {Hash} transitioned to {State}", hash, newState);
        }
    }

    public virtual async Task<IReadOnlyList<TrackedCandidate>> GetCandidatesAsync(CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, candidate_hash, state, source, content, created_at, updated_at
            FROM pipeline_states
            ORDER BY created_at DESC
            LIMIT 100
            """;

        return await ReadCandidatesAsync(cmd, ct);
    }

    public virtual async Task<IReadOnlyList<TrackedCandidate>> GetByStateAsync(CandidateState state, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, candidate_hash, state, source, content, created_at, updated_at
            FROM pipeline_states
            WHERE state = $state
            ORDER BY created_at DESC
            LIMIT 100
            """;
        cmd.Parameters.AddWithValue("$state", state.ToString());

        return await ReadCandidatesAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<TrackedCandidate>> ReadCandidatesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<TrackedCandidate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new TrackedCandidate(
                Id: reader.GetString(0),
                CandidateHash: reader.GetString(1),
                State: Enum.Parse<CandidateState>(reader.GetString(2)),
                Source: reader.IsDBNull(3) ? "" : reader.GetString(3),
                Content: reader.GetString(4),
                CreatedAt: DateTime.Parse(reader.GetString(5)),
                UpdatedAt: DateTime.Parse(reader.GetString(6))));
        }
        return results;
    }

    private static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes)[..16];
    }
}
