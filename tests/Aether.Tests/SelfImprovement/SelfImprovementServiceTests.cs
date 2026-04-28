using Aether.Memory;
using Aether.SelfImprovement;
using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.SelfImprovement;

public class SelfImprovementServiceTests
{
    [Fact]
    public async Task RunDailyReview_ExecutesPhasesWithoutCrash()
    {
        var memory = new FakeMemorySystem();
        var skillEvo = new SkillEvolution(NullLogger<SkillEvolution>.Instance, Path.GetTempPath());
        var benchmark = new FakeBenchmarkGate { Passes = true };
        var tracker = new FakePipelineTracker();

        var service = new SelfImprovementService(
            memory, skillEvo, benchmark, tracker, Path.GetTempPath(),
            NullLogger<SelfImprovementService>.Instance);

        await service.RunDailyReviewAsync();

        // All phases ran without exception
        Assert.True(true);
    }

    [Fact]
    public async Task RunDailyReview_ContinuesWhenBenchmarkThrows()
    {
        var memory = new FakeMemorySystem();
        var skillEvo = new SkillEvolution(NullLogger<SkillEvolution>.Instance, Path.GetTempPath());
        var benchmark = new FakeBenchmarkGate { ThrowOnRun = true };
        var tracker = new FakePipelineTracker();

        var service = new SelfImprovementService(
            memory, skillEvo, benchmark, tracker, Path.GetTempPath(),
            NullLogger<SelfImprovementService>.Instance);

        // Should not throw
        await service.RunDailyReviewAsync();
        Assert.True(true);
    }

    [Fact]
    public async Task RunDailyReview_WithSessions_GeneratesReflections()
    {
        var memory = new FakeMemorySystemWithSessions();
        var skillEvo = new SkillEvolution(NullLogger<SkillEvolution>.Instance, Path.GetTempPath());
        var benchmark = new FakeBenchmarkGate { Passes = true };
        var tracker = new FakePipelineTracker();

        var service = new SelfImprovementService(
            memory, skillEvo, benchmark, tracker, Path.GetTempPath(),
            NullLogger<SelfImprovementService>.Instance);

        await service.RunDailyReviewAsync();

        // High-message-count sessions generate candidates
        Assert.NotEmpty(tracker.Tracked);
    }

    [Fact]
    public async Task RunDailyReview_ContinuesWhenPromotionThrows()
    {
        var memory = new FakeMemorySystem
        {
            ShouldThrowOnPromote = true,
            PromoteReturnsTrue = true,
            OnGetRecentSessions = (since, ct) => Task.FromResult<IReadOnlyList<SessionSummary>>(new[]
            {
                new SessionSummary("s1", "test", DateTime.UtcNow, null, 25)
            })
        };
        var skillEvo = new SkillEvolution(NullLogger<SkillEvolution>.Instance, Path.GetTempPath());
        var benchmark = new FakeBenchmarkGate { Passes = true };
        var tracker = new FakePipelineTracker();

        var service = new SelfImprovementService(
            memory, skillEvo, benchmark, tracker, Path.GetTempPath(),
            NullLogger<SelfImprovementService>.Instance);

        // Should not throw — promotion fails are caught
        await service.RunDailyReviewAsync();
        Assert.True(true);
    }

    private sealed class FakeMemorySystemWithSessions : FakeMemorySystem
    {
        public override Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(DateTime since, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<SessionSummary>>(new[]
            {
                new SessionSummary("s1", "test", DateTime.UtcNow, null, 25)
            });
        }
    }
}
