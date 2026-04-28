using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class SkillEvolutionTests
{
    private readonly SkillEvolution _evolution = new(NullLogger<SkillEvolution>.Instance);

    [Fact]
    public async Task RecordUsage_Helped_IncreasesConfidence()
    {
        await _evolution.RecordUsageAsync("pdf", "merge pdf", helped: true);

        var records = await _evolution.GetRecordsAsync("pdf");
        Assert.Single(records);
        Assert.True(records[0].Helped);
        Assert.Equal(0.1f, records[0].ConfidenceDelta);
    }

    [Fact]
    public async Task RecordUsage_NotHelped_DecreasesConfidence()
    {
        await _evolution.RecordUsageAsync("pdf", "merge pdf", helped: false);

        var records = await _evolution.GetRecordsAsync("pdf");
        Assert.Single(records);
        Assert.False(records[0].Helped);
        Assert.Equal(-0.15f, records[0].ConfidenceDelta);
    }

    [Fact]
    public async Task GetRecidivismCandidates_ThreeUnhelpful_ReturnsCandidate()
    {
        for (var i = 0; i < 3; i++)
        {
            await _evolution.RecordUsageAsync("broken-skill", $"test {i}", helped: false);
        }

        var candidates = await _evolution.GetRecidivismCandidatesAsync();

        Assert.Single(candidates);
        Assert.Contains("broken-skill", candidates[0].Content);
        Assert.Equal("recidivism", candidates[0].Source);
    }

    [Fact]
    public async Task GetRecidivismCandidates_MostlyHelpful_ReturnsEmpty()
    {
        await _evolution.RecordUsageAsync("good-skill", "test 1", helped: true);
        await _evolution.RecordUsageAsync("good-skill", "test 2", helped: true);
        await _evolution.RecordUsageAsync("good-skill", "test 3", helped: false);

        var candidates = await _evolution.GetRecidivismCandidatesAsync();

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task GetRecords_LimitsToRequestedCount()
    {
        for (var i = 0; i < 10; i++)
        {
            await _evolution.RecordUsageAsync("skill", $"test {i}", helped: true);
        }

        var records = await _evolution.GetRecordsAsync("skill", limit: 5);

        Assert.Equal(5, records.Count);
    }

    [Fact]
    public async Task GetRecords_UnknownSkill_ReturnsEmpty()
    {
        var records = await _evolution.GetRecordsAsync("nonexistent");
        Assert.Empty(records);
    }

    [Fact]
    public async Task GeneratePatchAsync_WritesFileWithCorrectFormat()
    {
        var patchesDir = Path.Combine(Path.GetTempPath(), "aether-test-patches");
        Directory.CreateDirectory(patchesDir);

        try
        {
            var evolution = new SkillEvolution(NullLogger<SkillEvolution>.Instance, patchesDir);
            var candidate = new Aether.Memory.PromotionCandidate(
                "Skill 'test-skill' flagged for recidivism: 3/10 unhelpful recent uses.",
                0.8f, 3, "recidivism", DateTime.UtcNow);

            await evolution.GeneratePatchAsync("test-skill", candidate);

            var files = Directory.GetFiles(patchesDir, "skill-patch-test-skill-*.md");
            Assert.Single(files);

            var content = await File.ReadAllTextAsync(files[0]);
            Assert.Contains("# Skill Patch: test-skill", content);
            Assert.Contains("confidence: 0.80", content);
            Assert.Contains("source: recidivism", content);
            Assert.Contains("## Issue", content);
            Assert.Contains("## Proposed Change", content);
            Assert.Contains("state: PROPOSED", content);
        }
        finally
        {
            Directory.Delete(patchesDir, recursive: true);
        }
    }
}
