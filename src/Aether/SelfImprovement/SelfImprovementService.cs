using System.Text;
using Aether.Memory;
using Aether.Skills;
using Microsoft.Extensions.Logging;

namespace Aether.SelfImprovement;

public class SelfImprovementService 
{
    private readonly FileMemory _memory;
    private readonly SkillEvolution _skillEvolution;
    private readonly BenchmarkGate _benchmarkGate;
    private readonly PipelineTracker _pipelineTracker;
    private readonly string _patchesPath;
    private readonly ILogger<SelfImprovementService> _logger;

    public SelfImprovementService(
        FileMemory memory,
        SkillEvolution skillEvolution,
        BenchmarkGate benchmarkGate,
        PipelineTracker pipelineTracker,
        string patchesPath,
        ILogger<SelfImprovementService> logger)
    {
        _memory = memory;
        _skillEvolution = skillEvolution;
        _benchmarkGate = benchmarkGate;
        _pipelineTracker = pipelineTracker;
        _patchesPath = patchesPath;
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

        if (sessions.Count == 0)
        {
            _logger.LogInformation("No sessions in last 24h, writing empty reflections");
            WriteReflectionsFile(Array.Empty<PromotionCandidate>());
            return Array.Empty<PromotionCandidate>();
        }

        var candidates = new List<PromotionCandidate>();
        var reflections = new StringBuilder();
        reflections.AppendLine($"# Daily Reflections — {DateTime.UtcNow:yyyy-MM-dd}");
        reflections.AppendLine();

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

        WriteReflectionsFile(candidates);
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

    private void WriteReflectionsFile(IReadOnlyList<PromotionCandidate> candidates)
    {
        Directory.CreateDirectory(_patchesPath);

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var filePath = Path.Combine(_patchesPath, $"reflections-{dateStr}.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# Daily Reflections — {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine($"Candidates identified: {candidates.Count}");
        sb.AppendLine();

        foreach (var candidate in candidates)
        {
            sb.AppendLine($"## {candidate.Source} | confidence={candidate.Confidence:F1} | evidence={candidate.EvidenceCount}");
            sb.AppendLine();
            sb.AppendLine(candidate.Content);
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString());
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
