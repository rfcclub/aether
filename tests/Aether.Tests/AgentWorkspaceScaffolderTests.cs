using Aether.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class AgentWorkspaceScaffolderTests : IDisposable
{
    private readonly string _tempDir;

    public AgentWorkspaceScaffolderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_ws_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Scaffold_creates_all_required_files()
    {
        var workspacePath = Path.Combine(_tempDir, "workspace");
        var scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);

        await scaffolder.ScaffoldAsync("testagent", workspacePath, interactive: false);

        Assert.True(File.Exists(Path.Combine(workspacePath, "SOUL.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "USER.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "IDENTITY.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "MEMORY.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "HEARTBEAT.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "AGENTS_GUARD.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "DREAMS.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "INTROSPECTION.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "TASK_INBOX.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "TASK_REPORT.md")));
        Assert.True(File.Exists(Path.Combine(workspacePath, ".aether.json")));
        Assert.True(Directory.Exists(Path.Combine(workspacePath, "memory")));
    }

    [Fact]
    public async Task Scaffold_does_not_overwrite_existing_files()
    {
        var workspacePath = Path.Combine(_tempDir, "workspace");
        Directory.CreateDirectory(workspacePath);
        var soulPath = Path.Combine(workspacePath, "SOUL.md");
        var originalContent = "Custom SOUL content";
        await File.WriteAllTextAsync(soulPath, originalContent);

        var scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);
        await scaffolder.ScaffoldAsync("testagent", workspacePath, interactive: false);

        var afterContent = await File.ReadAllTextAsync(soulPath);
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task Scaffold_heartbeat_contains_required_markers()
    {
        var workspacePath = Path.Combine(_tempDir, "workspace");
        var scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);

        await scaffolder.ScaffoldAsync("testagent", workspacePath, interactive: false);

        var heartbeat = await File.ReadAllTextAsync(Path.Combine(workspacePath, "HEARTBEAT.md"));
        Assert.Contains("TASK_INBOX.md", heartbeat);
        Assert.Contains("HEARTBEAT_OK", heartbeat);
    }

    [Fact]
    public async Task Scaffold_soul_contains_voice_sections()
    {
        var workspacePath = Path.Combine(_tempDir, "workspace");
        var scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);

        await scaffolder.ScaffoldAsync("testagent", workspacePath, interactive: false);

        var soul = await File.ReadAllTextAsync(Path.Combine(workspacePath, "SOUL.md"));
        Assert.Contains("Tone", soul, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rules", soul, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scaffold_creates_memory_directory()
    {
        var workspacePath = Path.Combine(_tempDir, "workspace");
        var scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);

        await scaffolder.ScaffoldAsync("testagent", workspacePath, interactive: false);

        var memoryDir = Path.Combine(workspacePath, "memory");
        Assert.True(Directory.Exists(memoryDir));
        Assert.Empty(Directory.GetFiles(memoryDir));
    }
}
