using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class FileToolsTests : IDisposable
{
    private readonly string _workspace;
    private readonly SandboxContext _sandbox;

    public FileToolsTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "aether_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspace);
        _sandbox = new SandboxContext(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { }
    }

    [Fact]
    public async Task ReadTool_ReadsFile()
    {
        var path = Path.Combine(_workspace, "test.txt");
        await File.WriteAllTextAsync(path, "hello world");

        var tool = new ReadTool(NullLogger<ReadTool>.Instance);
        var args = JsonDocument.Parse($"{{\"path\":\"test.txt\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, _sandbox, CancellationToken.None);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ReadTool_PathOutsideWorkspace_Throws()
    {
        var tool = new ReadTool(NullLogger<ReadTool>.Instance);
        var args = JsonDocument.Parse($"{{\"path\":\"/etc/passwd\"}}").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, _sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task WriteTool_WritesFile()
    {
        var tool = new WriteTool(NullLogger<WriteTool>.Instance);
        var sandbox = new SandboxContext(_workspace, new Aether.Config.SpecToolsSection
        {
            File = new Aether.Config.SpecFileTool { AllowWrites = true }
        });

        var args = JsonDocument.Parse($"{{\"path\":\"out.txt\",\"content\":\"test content\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, sandbox, CancellationToken.None);

        Assert.Equal("Written.", result);
        Assert.True(File.Exists(Path.Combine(_workspace, "out.txt")));
    }

    [Fact]
    public async Task WriteTool_NoAllowWrites_Throws()
    {
        var tool = new WriteTool(NullLogger<WriteTool>.Instance);
        var args = JsonDocument.Parse($"{{\"path\":\"out.txt\",\"content\":\"test\"}}").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, _sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task EditTool_ReplacesText()
    {
        var path = Path.Combine(_workspace, "edit.txt");
        await File.WriteAllTextAsync(path, "hello world");

        var tool = new EditTool(NullLogger<EditTool>.Instance);
        var sandbox = new SandboxContext(_workspace, new Aether.Config.SpecToolsSection
        {
            File = new Aether.Config.SpecFileTool { AllowWrites = true }
        });

        var args = JsonDocument.Parse($"{{\"path\":\"edit.txt\",\"old_string\":\"hello\",\"new_string\":\"hi\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, sandbox, CancellationToken.None);

        Assert.Equal("Edited.", result);
        Assert.Equal("hi world", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task GlobTool_FindsFiles()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.cs"), "");
        File.WriteAllText(Path.Combine(_workspace, "b.cs"), "");
        File.WriteAllText(Path.Combine(_workspace, "readme.md"), "");

        var tool = new GlobTool(NullLogger<GlobTool>.Instance);
        var args = JsonDocument.Parse($"{{\"pattern\":\"*.cs\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, _sandbox, CancellationToken.None);
        var output = result.ToString()!;

        Assert.Contains("a.cs", output);
        Assert.Contains("b.cs", output);
        Assert.DoesNotContain("readme.md", output);
    }

    [Fact]
    public async Task GrepTool_FindsMatches()
    {
        File.WriteAllText(Path.Combine(_workspace, "code.cs"), "// TODO: fix this\n// ok");

        var tool = new GrepTool(NullLogger<GrepTool>.Instance);
        var args = JsonDocument.Parse($"{{\"pattern\":\"TODO\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, _sandbox, CancellationToken.None);
        var output = result.ToString()!;

        Assert.Contains("TODO", output);
        Assert.Contains("code.cs", output);
    }
}
