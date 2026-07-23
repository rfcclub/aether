using Aether.Data;
using Aether.Memory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aether.Skills;

public record SkillEvolutionRecord(
    string SkillName,
    string UserMessage,
    bool Helped,
    float ConfidenceDelta,
    DateTime RecordedAt
);

public class SkillEvolution 
{
    private readonly List<SkillEvolutionRecord> _records = new();
    private readonly object _lock = new();
    private readonly ILogger<SkillEvolution> _logger;
    private readonly string _patchesPath;
    private readonly AetherDb? _db;

    // Recidivism: skill marked unhelpful 3+ times
    private const int RecidivismThreshold = 3;
    private const float MinConfidenceForPromotion = 0.7f;

    public SkillEvolution(ILogger<SkillEvolution> logger, string patchesPath = "patches", AetherDb? db = null)
    {
        _logger = logger;
        _patchesPath = patchesPath;
        _db = db;

        if (_db is not null)
        {
            _ = LoadHistoryAsync();
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            await using var conn = await _db!.OpenConnectionAsync(CancellationToken.None);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT skill_name, user_message, helped, confidence_delta, recorded_at
                FROM skill_evolution
                ORDER BY recorded_at ASC
                LIMIT 5000
                """;
            await using var reader = await cmd.ExecuteReaderAsync(CancellationToken.None);
            while (await reader.ReadAsync(CancellationToken.None))
            {
                _records.Add(new SkillEvolutionRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) == 1,
                    (float)reader.GetDouble(3),
                    DateTime.Parse(reader.GetString(4))));
            }
            _logger.LogInformation("Loaded {Count} skill evolution records from database", _records.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skill evolution history — starting fresh");
        }
    }

    public async Task RecordUsageAsync(string skillName, string userMessage, bool helped, CancellationToken ct = default)
    {
        var delta = helped ? 0.1f : -0.15f;  // helped += 0.1, not helped -= 0.15 (asymmetric)
        var record = new SkillEvolutionRecord(skillName, userMessage, helped, delta, DateTime.UtcNow);

        lock (_lock)
        {
            _records.Add(record);

            // Keep only last 100 records per skill
            var skillRecords = _records.Where(r => r.SkillName == skillName).ToList();
            if (skillRecords.Count > 100)
            {
                var toRemove = skillRecords.Count - 100;
                _records.RemoveRange(0, toRemove);
            }
        }

        // Persist to database
        if (_db is not null)
        {
            try
            {
                await using var conn = await _db.OpenConnectionAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO skill_evolution (skill_name, user_message, helped, confidence_delta, recorded_at)
                    VALUES ($name, $msg, $helped, $delta, $at)
                    """;
                cmd.Parameters.AddWithValue("$name", skillName);
                cmd.Parameters.AddWithValue("$msg", userMessage);
                cmd.Parameters.AddWithValue("$helped", helped ? 1 : 0);
                cmd.Parameters.AddWithValue("$delta", (double)delta);
                cmd.Parameters.AddWithValue("$at", record.RecordedAt.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist skill evolution record for {SkillName}", skillName);
            }
        }

        _logger.LogInformation("Skill evolution: {SkillName} -> helped={Helped}, delta={Delta:F2}",
            skillName, helped, delta);

        return;
    }

    public Task<IReadOnlyList<SkillEvolutionRecord>> GetRecordsAsync(string skillName, int limit = 20, CancellationToken ct = default)
    {
        List<SkillEvolutionRecord> result;
        lock (_lock)
        {
            result = _records
                .Where(r => r.SkillName == skillName)
                .OrderByDescending(r => r.RecordedAt)
                .Take(limit)
                .ToList();
        }
        return Task.FromResult<IReadOnlyList<SkillEvolutionRecord>>(result);
    }

    public async Task GeneratePatchAsync(string skillName, PromotionCandidate candidate, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_patchesPath);

        var safeName = skillName.Replace('/', '-').Replace('\\', '-');
        var dateStr = candidate.CreatedAt.ToString("yyyy-MM-dd");
        var fileName = $"skill-patch-{safeName}-{dateStr}.md";
        var filePath = Path.Combine(_patchesPath, fileName);

        var patch = $"""
            # Skill Patch: {skillName}
            date: {candidate.CreatedAt:O}
            confidence: {candidate.Confidence:F2}
            evidence_count: {candidate.EvidenceCount}
            source: {candidate.Source}
            state: PROPOSED

            ## Issue
            {candidate.Content}

            ## Proposed Change
            <!-- Human reviewer: edit the SKILL.md based on the issue above, then mark state as APPLIED -->

            """;

        await File.WriteAllTextAsync(filePath, patch, ct);
        _logger.LogInformation("Generated skill patch: {Path}", filePath);
    }

    public Task<IReadOnlyList<PromotionCandidate>> GetRecidivismCandidatesAsync(CancellationToken ct = default)
    {
        var candidates = new List<PromotionCandidate>();

        lock (_lock)
        {
            var skillGroups = _records.GroupBy(r => r.SkillName);
            foreach (var group in skillGroups)
            {
                var recent = group.OrderByDescending(r => r.RecordedAt).Take(10).ToList();
                var unhelpfulCount = recent.Count(r => !r.Helped);

                if (unhelpfulCount >= RecidivismThreshold)
                {
                    var avgDelta = recent.Average(r => r.ConfidenceDelta);
                    // Only promote if overall trend is negative (skill making things worse)
                    if (avgDelta < 0)
                    {
                        candidates.Add(new PromotionCandidate(
                            Content: $"Skill '{group.Key}' flagged for recidivism: {unhelpfulCount}/10 unhelpful recent uses. Consider disabling or revising.",
                            Confidence: MinConfidenceForPromotion,
                            EvidenceCount: unhelpfulCount,
                            Source: "recidivism",
                            CreatedAt: DateTime.UtcNow
                        ));
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<PromotionCandidate>>(candidates);
    }
}