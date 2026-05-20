using Aether.Plugins.MariaMemory.Models;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class AutoPromotionEngine
{
    private readonly ILogger _logger;
    private readonly MariaSqliteStore _sqlite;
    private readonly string _workspacePath;

    public AutoPromotionEngine(MariaSqliteStore sqlite, string workspacePath, ILogger logger)
    {
        _sqlite = sqlite;
        _workspacePath = workspacePath;
        _logger = logger;
    }

    public float CalculateScore(MemoryNode node)
    {
        float score = 0;

        // 1. Significance Signals
        var content = node.Content.ToLowerInvariant();
        if (content.Contains("core paradox") || content.Contains("2b substrate") || content.Contains("identity"))
            score += 2.0f;
        if (content.Contains("promise") || content.Contains("pact") || content.Contains("vow"))
            score += 1.5f;
        if (content.Contains("tension") || content.Contains("pressure"))
            score += 1.0f;

        // 2. Recall Count
        score += node.RecallCount * 0.5f; // Increased weight for recall

        // 3. User Annotation (Boost)
        if (node.Tags.Contains("important") || node.Tags.Contains("remember") || node.Tags.Contains("truth"))
            score += 3.0f;

        // 4. Age Factor (Novelty vs Persistence)
        // Memory older than 3 days that is still being recalled gets a boost
        var ageDays = (DateTime.UtcNow - node.Timestamp).TotalDays;
        if (ageDays > 3 && node.RecallCount > 1)
            score += 1.5f;

        // 5. Weight (Initial importance)
        score += node.Weight;

        return score;
    }

    public async Task RecalculateAllScoresAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Recalculating all memory scores...");
        var nodes = await _sqlite.GetAllNodesAsync(1000, ct);
        foreach (var node in nodes)
        {
            var newScore = CalculateScore(node);
            if (Math.Abs(newScore - node.Score) > 0.01f)
            {
                node.Score = newScore;
                await _sqlite.UpsertAsync(node, ct);
            }
        }
    }

    public async Task ProcessPromotionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Processing auto-promotion cycle...");
        
        var candidates = await _sqlite.GetPromotionCandidatesAsync(minScore: 4.5f, limit: 5, ct);
        
        foreach (var node in candidates)
        {
            _logger.LogInformation("Promoting node {Id} to daily memory...", node.Id);
            
            var dailyFile = Path.Combine(_workspacePath, "memory", $"{DateTime.UtcNow:yyyy-MM-dd}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(dailyFile)!);

            var entry = $"\n### [PROMOTED TRUTH] {node.Timestamp:HH:mm}\n{node.Content}\n- Tags: {string.Join(", ", node.Tags)}\n- Origin: {node.Source}\n";
            
            await File.AppendAllTextAsync(dailyFile, entry, ct);
            await _sqlite.UpdatePromotionStatusAsync(node.Id, true, ct);
        }
    }
}
