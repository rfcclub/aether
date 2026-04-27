using Aether.Memory;
using Microsoft.Extensions.Logging;

namespace Aether.Skills;

public interface ISkillEvolution
{
    Task RecordUsageAsync(string skillName, string userMessage, bool helped, CancellationToken ct = default);
    Task<IReadOnlyList<SkillEvolutionRecord>> GetRecordsAsync(string skillName, int limit = 20, CancellationToken ct = default);
    Task<IReadOnlyList<PromotionCandidate>> GetRecidivismCandidatesAsync(CancellationToken ct = default);
}

public record SkillEvolutionRecord(
    string SkillName,
    string UserMessage,
    bool Helped,
    float ConfidenceDelta,
    DateTime RecordedAt
);

public class SkillEvolution : ISkillEvolution
{
    private readonly List<SkillEvolutionRecord> _records = new();
    private readonly object _lock = new();
    private readonly ILogger<SkillEvolution> _logger;

    // Recidivism: skill marked unhelpful 3+ times
    private const int RecidivismThreshold = 3;
    private const float MinConfidenceForPromotion = 0.7f;

    public SkillEvolution(ILogger<SkillEvolution> logger)
    {
        _logger = logger;
    }

    public Task RecordUsageAsync(string skillName, string userMessage, bool helped, CancellationToken ct = default)
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

        _logger.LogInformation("Skill evolution: {SkillName} -> helped={Helped}, delta={Delta:F2}",
            skillName, helped, delta);

        return Task.CompletedTask;
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