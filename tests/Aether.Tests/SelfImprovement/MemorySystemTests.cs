using Aether.Data;
using Aether.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.SelfImprovement;

public class MemorySystemTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _memoryFilePath;
    private readonly SqliteMemorySystem _memory;

    public MemorySystemTests()
    {
        _dbPath = Path.GetTempFileName();
        _memoryFilePath = Path.GetTempFileName();

        // Initialize schema via AetherDb
        var schemaPath = FindSchemaPath();
        var db = new AetherDb(_dbPath, schemaPath);
        db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        _memory = new SqliteMemorySystem(_dbPath, _memoryFilePath, NullLogger<SqliteMemorySystem>.Instance);
        _memory.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_memoryFilePath); } catch { }
    }

    private static string FindSchemaPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aether", "Data", "Schema.sql"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("Cannot find Schema.sql");
    }

    [Fact]
    public async Task TryPromoteAsync_RejectsCandidateBelowConfidenceThreshold()
    {
        var candidate = new PromotionCandidate(
            "test content",
            Confidence: 0.5f,      // Below 0.7 threshold
            EvidenceCount: 5,
            Source: "reflection",
            CreatedAt: DateTime.UtcNow);

        var result = await _memory.TryPromoteAsync(candidate);
        Assert.False(result);
    }

    [Fact]
    public async Task TryPromoteAsync_RejectsCandidateBelowEvidenceThreshold()
    {
        var candidate = new PromotionCandidate(
            "test content",
            Confidence: 0.9f,
            EvidenceCount: 1,      // Below 3 threshold
            Source: "reflection",
            CreatedAt: DateTime.UtcNow);

        var result = await _memory.TryPromoteAsync(candidate);
        Assert.False(result);
    }

    [Fact]
    public async Task TryPromoteAsync_AcceptsValidCandidate()
    {
        var candidate = new PromotionCandidate(
            "valid entry for memory storage",
            Confidence: 0.9f,
            EvidenceCount: 5,
            Source: "reflection",
            CreatedAt: DateTime.UtcNow);

        var result = await _memory.TryPromoteAsync(candidate);
        Assert.True(result);

        var durable = await _memory.GetDurableMemoryAsync();
        Assert.Contains("valid entry for memory storage", durable);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_ReturnsRecentSessions()
    {
        // Create a session with activity now
        var sessionId = await _memory.CreateSessionAsync("test-agent");
        await _memory.AppendMessageAsync(sessionId, "user", "test message");

        var recent = await _memory.GetRecentSessionsAsync(DateTime.UtcNow.AddHours(-1));
        Assert.Contains(recent, s => s.Id == sessionId);
    }

    [Fact]
    public async Task GetRecentSessionsAsync_ExcludesOldSessions()
    {
        var sessionId = await _memory.CreateSessionAsync("test-agent");
        await _memory.AppendMessageAsync(sessionId, "user", "test message");

        var recent = await _memory.GetRecentSessionsAsync(DateTime.UtcNow.AddHours(1));
        Assert.DoesNotContain(recent, s => s.Id == sessionId);
    }
}
