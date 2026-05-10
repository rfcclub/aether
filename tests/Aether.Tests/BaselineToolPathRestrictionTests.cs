using System.Text.Json;
using Aether.Config;
using Aether.Tooling;

namespace Aether.Tests;

/// <summary>
/// Tests for workspace path restrictions on OpenClaw baseline tools.
/// Covers tasks 4.8–4.12 from unify-tool-dispatch.
/// </summary>
public sealed class BaselineToolPathRestrictionTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _memoryDir;

    public BaselineToolPathRestrictionTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"aether_restrict_{Guid.NewGuid():N}");
        _memoryDir = Path.Combine(_workspace, "memory");
        Directory.CreateDirectory(_memoryDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { }
    }

    private SandboxContext PermissiveSandbox() =>
        new SandboxContext(_workspace, new SpecToolsSection
        {
            File = new SpecFileTool { AllowWrites = true }
        });

    private SandboxContext RestrictiveSandbox(string deniedPath) =>
        new SandboxContext(_workspace, new SpecToolsSection
        {
            File = new SpecFileTool
            {
                AllowWrites = true,
                DeniedPaths = new List<string> { deniedPath }
            }
        });

    private SandboxContext NoWriteSandbox() =>
        new SandboxContext(_workspace, new SpecToolsSection
        {
            File = new SpecFileTool { AllowWrites = false }
        });

    // ── 4.8: skill_read rejects path traversal ──

    [Fact]
    public async Task SkillRead_RejectsDotDotTraversal()
    {
        var tool = new SkillReadTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"name": "../etc/passwd"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task SkillRead_RejectsSlashInName()
    {
        var tool = new SkillReadTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"name": "foo/bar"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task SkillRead_RejectsBackslashInName()
    {
        var tool = new SkillReadTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"name": "foo\\bar"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task SkillRead_RejectsEmptyName()
    {
        var tool = new SkillReadTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"name": ""}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    // ── 4.9: memory_read rejects path outside memory/ ──

    [Fact]
    public async Task MemoryRead_RejectsTraversalOutsideMemory()
    {
        var tool = new MemoryReadTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"path": "../../etc/passwd"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryRead_RejectsRootedPath()
    {
        var tool = new MemoryReadTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"path": "/etc/passwd"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryRead_AllowsValidRelativePath()
    {
        var tool = new MemoryReadTool();
        var sandbox = PermissiveSandbox();

        var notePath = Path.Combine(_memoryDir, "note.md");
        await File.WriteAllTextAsync(notePath, "memory content");
        var args = JsonDocument.Parse("""{"path": "note.md"}""").RootElement;

        var result = await tool.ExecuteAsync(args, sandbox, CancellationToken.None);
        Assert.Equal("memory content", result);
    }

    // ── 4.10: memory_write rejects when AllowWrites is false ──

    [Fact]
    public async Task MemoryWrite_RejectsWhenWritesDisabled()
    {
        var tool = new MemoryWriteTool();
        var sandbox = NoWriteSandbox();

        var args = JsonDocument.Parse("""{"content": "test note"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryWrite_AllowsWhenWritesEnabled()
    {
        var tool = new MemoryWriteTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"content": "test note", "append": false}""").RootElement;

        var result = await tool.ExecuteAsync(args, sandbox, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Contains("Wrote memory/", result.ToString()!);

        // Verify file was actually written
        var today = $"{DateTime.UtcNow:yyyy-MM-dd}.md";
        var written = await File.ReadAllTextAsync(Path.Combine(_memoryDir, today));
        Assert.Equal("test note", written);
    }

    // ── 4.11: memory_write rejects path outside memory/ ──

    [Fact]
    public async Task MemoryWrite_RejectsTraversalOutsideMemory()
    {
        var tool = new MemoryWriteTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"content": "hacked", "path": "../../etc/cron.d/evil"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task MemoryWrite_RejectsRootedPath()
    {
        var tool = new MemoryWriteTool();
        var sandbox = PermissiveSandbox();

        var args = JsonDocument.Parse("""{"content": "hacked", "path": "/tmp/evil.md"}""").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    // ── 4.12: memory_search skips denied paths ──

    [Fact]
    public async Task MemorySearch_SkipsDeniedPaths()
    {
        // Create a subdir with a file that matches
        var subdir = Path.Combine(_memoryDir, "secret");
        Directory.CreateDirectory(subdir);
        await File.WriteAllTextAsync(Path.Combine(subdir, "hidden.md"), "hidden secret data");
        await File.WriteAllTextAsync(Path.Combine(_memoryDir, "visible.md"), "visible secret data");

        // Deny the secret subdirectory
        var sandbox = RestrictiveSandbox(subdir);

        var tool = new MemorySearchTool();
        var args = JsonDocument.Parse("""{"query": "secret"}""").RootElement;

        var result = (string)await tool.ExecuteAsync(args, sandbox, CancellationToken.None);

        // Should find "visible" but not "hidden"
        Assert.Contains("visible.md", result);
        Assert.DoesNotContain("hidden.md", result);
    }

    [Fact]
    public async Task MemorySearch_ReturnsResultsFromPermittedPaths()
    {
        await File.WriteAllTextAsync(Path.Combine(_memoryDir, "log.md"), "found keyword here");

        var sandbox = PermissiveSandbox();
        var tool = new MemorySearchTool();
        var args = JsonDocument.Parse("""{"query": "keyword"}""").RootElement;

        var result = (string)await tool.ExecuteAsync(args, sandbox, CancellationToken.None);

        Assert.Contains("log.md", result);
        Assert.Contains("keyword", result);
    }

    [Fact]
    public async Task MemorySearch_ReturnsEmptyWhenNoMatches()
    {
        await File.WriteAllTextAsync(Path.Combine(_memoryDir, "log.md"), "nothing here");

        var sandbox = PermissiveSandbox();
        var tool = new MemorySearchTool();
        var args = JsonDocument.Parse("""{"query": "xyznothere"}""").RootElement;

        var result = (string)await tool.ExecuteAsync(args, sandbox, CancellationToken.None);

        Assert.Contains("No memory matches", result);
    }
}
