using Aether.Agents;

namespace Aether.Tests;

public sealed class AgentMemoryBridgeTests : IDisposable
{
    private readonly string _agentDir;

    public AgentMemoryBridgeTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-mem-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task AppendDailyMemoryAsync_WritesToCorrectFile()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        await bridge.AppendDailyMemoryAsync("New session started.", "test-session");

        var filePath = Path.Combine(_agentDir, "memory", $"{today}.md");
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("New session started.", content);
        Assert.Contains("test-session", content);
    }

    [Fact]
    public async Task ReadLongTermMemoryAsync_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_agentDir, "MEMORY.md"), "Long-term memories here.");
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        var result = await bridge.ReadLongTermMemoryAsync();

        Assert.Equal("Long-term memories here.", result);
    }

    [Fact]
    public async Task ReadLongTermMemoryAsync_ReturnsEmptyWhenMissing()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        var result = await bridge.ReadLongTermMemoryAsync();

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task WriteLongTermMemoryAsync_OverwritesFile()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        await bridge.WriteLongTermMemoryAsync("Updated memories.");
        var result = await File.ReadAllTextAsync(Path.Combine(_agentDir, "MEMORY.md"));

        Assert.Equal("Updated memories.", result);
    }

    [Fact]
    public async Task ReadTaskInboxAsync_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_agentDir, "TASK_INBOX.md"), "- [ ] Task 1\n- [ ] Task 2");
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        var result = await bridge.ReadTaskInboxAsync();

        Assert.Contains("Task 1", result);
    }

    [Fact]
    public async Task WriteTaskReportAsync_WritesToReportFile()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        await bridge.WriteTaskReportAsync("Task 1 completed.");

        var content = await File.ReadAllTextAsync(Path.Combine(_agentDir, "TASK_REPORT.md"));
        Assert.Contains("Task 1 completed.", content);
    }
}
