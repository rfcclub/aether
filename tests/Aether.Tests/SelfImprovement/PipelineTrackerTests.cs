using Aether.Data;
using Aether.Memory;
using Aether.SelfImprovement;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.SelfImprovement;

public class PipelineTrackerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AetherDb _db;
    private readonly PipelineTracker _tracker;

    public PipelineTrackerTests()
    {
        _dbPath = Path.GetTempFileName();
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql");

        // If schema not found at output path, try project-relative
        if (!File.Exists(schemaPath))
        {
            schemaPath = FindSchemaPath();
        }

        _db = new AetherDb(_dbPath, schemaPath);
        _db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _tracker = new PipelineTracker(_db, NullLogger<PipelineTracker>.Instance);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task TrackAsync_InsertsCandidateWithProposedState()
    {
        var candidate = new PromotionCandidate("test content", 0.8f, 3, "reflection", DateTime.UtcNow);
        await _tracker.TrackAsync(candidate);

        var all = await _tracker.GetCandidatesAsync();
        Assert.Single(all);
        Assert.Equal(CandidateState.PROPOSED, all[0].State);
        Assert.Equal("reflection", all[0].Source);
    }

    [Fact]
    public async Task TransitionAsync_UpdatesStateFromProposedToApplied()
    {
        var candidate = new PromotionCandidate("test content", 0.8f, 3, "reflection", DateTime.UtcNow);
        await _tracker.TrackAsync(candidate);
        await _tracker.TransitionAsync(candidate, CandidateState.APPLIED);

        var byState = await _tracker.GetByStateAsync(CandidateState.APPLIED);
        Assert.Single(byState);
    }

    [Fact]
    public async Task GetByState_FiltersCorrectly()
    {
        var c1 = new PromotionCandidate("content a", 0.8f, 3, "reflection", DateTime.UtcNow);
        var c2 = new PromotionCandidate("content b", 0.7f, 3, "recidivism", DateTime.UtcNow);

        await _tracker.TrackAsync(c1);
        await _tracker.TrackAsync(c2);
        await _tracker.TransitionAsync(c1, CandidateState.APPLIED);
        await _tracker.TransitionAsync(c2, CandidateState.FAILED);

        var applied = await _tracker.GetByStateAsync(CandidateState.APPLIED);
        var failed = await _tracker.GetByStateAsync(CandidateState.FAILED);
        var verified = await _tracker.GetByStateAsync(CandidateState.VERIFIED);

        Assert.Single(applied);
        Assert.Single(failed);
        Assert.Empty(verified);
    }

    [Fact]
    public async Task TrackAsync_DuplicateContent_DoesNotDuplicate()
    {
        var candidate = new PromotionCandidate("same content", 0.8f, 3, "reflection", DateTime.UtcNow);
        await _tracker.TrackAsync(candidate);
        await _tracker.TrackAsync(candidate); // duplicate

        var all = await _tracker.GetCandidatesAsync();
        Assert.Single(all);
    }

    private static string FindSchemaPath()
    {
        var cwd = Environment.CurrentDirectory;
        for (var i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(cwd, "src", "Aether", "Data", "Schema.sql");
            if (File.Exists(candidate)) return candidate;
            cwd = Path.GetDirectoryName(cwd) ?? cwd;
        }
        throw new FileNotFoundException("Cannot find Schema.sql");
    }
}
