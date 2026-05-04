using Aether.Agents;

namespace Aether.Tests;

public sealed class EpisodicLoggerTests : IDisposable
{
    private readonly string _agentDir;

    public EpisodicLoggerTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-ep-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task AppendEpisodeAsync_WritesCanonicalSchema()
    {
        var logger = new EpisodicLogger(_agentDir, new BootConfig());
        var id = await logger.AppendEpisodeAsync(
            "session-1", "user", "Resolved the login bug by fixing token expiry.",
            new Dictionary<string, string> { ["topic"] = "debugging" });

        Assert.StartsWith("mem_", id);
        var logPath = Path.Combine(_agentDir, "INTROSPECTION.md");
        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("type: episode", content);
        Assert.Contains("Resolved the login bug", content);
        Assert.Contains("session-1", content);
    }

    [Fact]
    public async Task AppendEpisodeAsync_IncrementsSequenceNumber()
    {
        var logger = new EpisodicLogger(_agentDir, new BootConfig());
        var id1 = await logger.AppendEpisodeAsync("s1", "user", "First.");
        var id2 = await logger.AppendEpisodeAsync("s1", "assistant", "Second.");

        Assert.NotEqual(id1, id2);
        Assert.Contains(DateTime.UtcNow.ToString("yyyyMMdd"), id1);
        Assert.Contains(DateTime.UtcNow.ToString("yyyyMMdd"), id2);
    }

    [Fact]
    public async Task AppendMistakeAsync_UsesMistakesFile()
    {
        var config = new BootConfig { MistakesFile = "MISTAKES.md" };
        var logger = new EpisodicLogger(_agentDir, config);
        await logger.AppendMistakeAsync("session-2", "Forgot to validate null input.");

        var path = Path.Combine(_agentDir, "MISTAKES.md");
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("type: mistake", content);
        Assert.Contains("Forgot to validate", content);
    }
}
