using System.Text;
using Aether.Data;
using Aether.Memory;
using Aether.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.SelfImprovement;

public class SelfImprovementService 
{
    private readonly FileMemory _memory;
    private readonly SkillEvolution _skillEvolution;
    private readonly BenchmarkGate _benchmarkGate;
    private readonly PipelineTracker _pipelineTracker;
    private readonly string _patchesPath;
    private readonly IServiceProvider _services;
    private readonly ILogger<SelfImprovementService> _logger;

    public SelfImprovementService(
        FileMemory memory,
        SkillEvolution skillEvolution,
        BenchmarkGate benchmarkGate,
        PipelineTracker pipelineTracker,
        string patchesPath,
        IServiceProvider services,
        ILogger<SelfImprovementService> logger)
    {
        _memory = memory;
        _skillEvolution = skillEvolution;
        _benchmarkGate = benchmarkGate;
        _pipelineTracker = pipelineTracker;
        _patchesPath = patchesPath;
        _services = services;
        _logger = logger;
    }

    public async Task RunDailyReviewAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        _logger.LogInformation("Daily review started at {Time}", startedAt);

        // Phase 1: Reflection
        var reflectionCandidates = await RunPhaseAsync("Reflection",
            () => GenerateReflectionsAsync(ct));

        // Phase 2: Promotion
        var allCandidates = reflectionCandidates ?? new List<PromotionCandidate>();
        var recidivismCandidates = await RunPhaseAsync("Recidivism",
            () => _skillEvolution.GetRecidivismCandidatesAsync(ct));
        if (recidivismCandidates is not null)
        {
            allCandidates = allCandidates.Concat(recidivismCandidates).ToList();
        }

        var promotedCount = 0;
        foreach (var candidate in allCandidates)
        {
            try
            {
                await _pipelineTracker.TrackAsync(candidate, ct);

                var promoted = await _memory.TryPromoteAsync(candidate, ct);
                if (promoted)
                {
                    await _pipelineTracker.TransitionAsync(candidate, CandidateState.APPLIED, ct);
                    promotedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Promotion failed for candidate from {Source}", candidate.Source);
            }
        }
        _logger.LogInformation("Promotion phase: {Promoted}/{Total} candidates promoted",
            promotedCount, allCandidates.Count);

        // Phase 3: Patch generation
        await RunPhaseAsync("PatchGeneration", async () =>
        {
            foreach (var candidate in allCandidates)
            {
                var skillName = candidate.Source == "recidivism"
                    ? ExtractSkillName(candidate.Content)
                    : "general";
                await _skillEvolution.GeneratePatchAsync(skillName, candidate, ct);
            }
        });

        // Phase 4: Benchmark
        if (allCandidates.Count > 0)
        {
            await RunPhaseAsync("Benchmark", async () =>
            {
                var result = await _benchmarkGate.RunTestsAsync(ct);

                foreach (var candidate in allCandidates)
                {
                    var newState = result.Passed ? CandidateState.VERIFIED : CandidateState.FAILED;
                    await _pipelineTracker.TransitionAsync(candidate, newState, ct);
                }
            });

            // Phase 5: Surface VERIFIED patches
            await RunPhaseAsync("Surfacing", async () =>
            {
                var verified = await _pipelineTracker.GetByStateAsync(CandidateState.VERIFIED, ct);
                foreach (var candidate in verified)
                {
                    _logger.LogWarning(
                        "[REVIEW] VERIFIED patch ready for human review: {Hash} from {Source}. Content: {Content}",
                        candidate.CandidateHash, candidate.Source, candidate.Content);
                }

                if (verified.Count == 0)
                {
                    _logger.LogInformation("No VERIFIED patches to surface");
                }
            });
        }

        var duration = DateTime.UtcNow - startedAt;
        _logger.LogInformation("Daily review completed in {Duration}", duration);
    }

    private async Task<IReadOnlyList<PromotionCandidate>> GenerateReflectionsAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var sessions = await _memory.GetRecentSessionsAsync(since, ct);

        var candidates = new List<PromotionCandidate>();
        var reflections = new StringBuilder();
        reflections.AppendLine($"# Daily Reflections — {DateTime.UtcNow:yyyy-MM-dd}");
        reflections.AppendLine();

        // Include Goal Progress
        try
        {
            using var scope = _services.CreateScope();
            var goalStore = scope.ServiceProvider.GetRequiredService<GoalStore>();
            var activeGoals = await goalStore.GetActiveGoalsAsync("maria", ct);
            reflections.AppendLine("## Goal Progress");
            if (activeGoals.Count == 0)
            {
                reflections.AppendLine("- No active goals.");
            }
            else
            {
                foreach (var goal in activeGoals)
                {
                    reflections.AppendLine($"- [{goal.Status}] {goal.Title}: {goal.Description} (Priority: {goal.Priority})");
                }
            }
            reflections.AppendLine();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load goals during daily reflection");
        }

        if (sessions.Count == 0)
        {
            _logger.LogInformation("No sessions in last 24h.");
            reflections.AppendLine("No sessions in last 24h.");
            WriteReflectionsFile(Array.Empty<PromotionCandidate>(), reflections.ToString());
            return Array.Empty<PromotionCandidate>();
        }

        foreach (var session in sessions)
        {
            var frictionPoints = await ScanSessionForFrictionAsync(session, ct);
            if (frictionPoints.Count > 0)
            {
                reflections.AppendLine($"## Session {session.Id} ({session.StartedAt:O})");
                reflections.AppendLine();

                foreach (var point in frictionPoints)
                {
                    reflections.AppendLine($"- {point}");
                    candidates.Add(new PromotionCandidate(
                        Content: $"[Session {session.Id}] {point}",
                        Confidence: 0.8f,
                        EvidenceCount: 3,
                        Source: "reflection",
                        CreatedAt: DateTime.UtcNow));
                }

                reflections.AppendLine();
            }
        }

        if (candidates.Count == 0)
        {
            reflections.AppendLine("No friction signals detected.");
        }

        WriteReflectionsFile(candidates, reflections.ToString());
        return candidates;
    }

    private static async Task<IReadOnlyList<string>> ScanSessionForFrictionAsync(
        SessionSummary session, CancellationToken ct)
    {
        // SessionSummary doesn't carry messages. Friction detection uses
        // session metadata — sessions with many messages but no user feedback
        // pattern indicate potential friction.
        var points = new List<string>();

        await Task.CompletedTask; // placeholder for async pattern

        if (session.MessageCount > 20)
        {
            points.Add(
                $"High message count ({session.MessageCount}) — possible correction loop or excessive tool retries.");
        }

        return points;
    }

    private void WriteReflectionsFile(IReadOnlyList<PromotionCandidate> candidates, string content)
    {
        Directory.CreateDirectory(_patchesPath);

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var filePath = Path.Combine(_patchesPath, $"reflections-{dateStr}.md");

        File.WriteAllText(filePath, content);
        _logger.LogInformation("Wrote reflections to {Path}", filePath);
    }

    private async Task<T?> RunPhaseAsync<T>(string phaseName, Func<Task<T>> phase)
    {
        try
        {
            _logger.LogInformation("Phase {Phase} started", phaseName);
            var result = await phase();
            _logger.LogInformation("Phase {Phase} completed", phaseName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase {Phase} failed, continuing to next phase", phaseName);
            return default;
        }
    }

    private async Task RunPhaseAsync(string phaseName, Func<Task> phase)
    {
        try
        {
            _logger.LogInformation("Phase {Phase} started", phaseName);
            await phase();
            _logger.LogInformation("Phase {Phase} completed", phaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase {Phase} failed, continuing to next phase", phaseName);
        }
    }

    private static string ExtractSkillName(string content)
    {
        const string prefix = "Skill '";
        var start = content.IndexOf(prefix, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += prefix.Length;
            var end = content.IndexOf('\'', start);
            if (end > start)
            {
                return content[start..end];
            }
        }
        return "unknown";
    }
}
