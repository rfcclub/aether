using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class BashToolTests
{
    private readonly string _workspace;
    private readonly SandboxContext _sandbox;

    public BashToolTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "aether_bash_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspace);
        _sandbox = new SandboxContext(_workspace);
    }

    [Fact]
    public async Task SimpleCommand_ReturnsOutput()
    {
        var tool = new BashTool(NullLogger<BashTool>.Instance);
        var args = JsonDocument.Parse($"{{\"command\":\"echo hello\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, _sandbox, CancellationToken.None);
        var bashResult = Assert.IsType<BashTool.BashResult>(result);

        Assert.Equal(0, bashResult.ExitCode);
        Assert.Contains("hello", bashResult.Output);
    }

    [Fact]
    public async Task CommandWithError_ReturnsStderrAndExitCode()
    {
        var tool = new BashTool(NullLogger<BashTool>.Instance);
        var args = JsonDocument.Parse($"{{\"command\":\"ls /nonexistent_path_xyz\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, _sandbox, CancellationToken.None);
        var bashResult = Assert.IsType<BashTool.BashResult>(result);

        Assert.NotEqual(0, bashResult.ExitCode);
    }

    [Fact]
    public async Task DeniedCommand_Throws()
    {
        var tool = new BashTool(NullLogger<BashTool>.Instance);
        var sandbox = new SandboxContext(_workspace, new Aether.Config.SpecToolsSection
        {
            Shell = new Aether.Config.SpecShellTool { AllowedCommands = new List<string> { "echo", "ls" } }
        });

        var args = JsonDocument.Parse($"{{\"command\":\"curl example.com\"}}").RootElement;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task AllowedCommand_Executes()
    {
        var tool = new BashTool(NullLogger<BashTool>.Instance);
        var sandbox = new SandboxContext(_workspace, new Aether.Config.SpecToolsSection
        {
            Shell = new Aether.Config.SpecShellTool { AllowedCommands = new List<string> { "echo" } }
        });

        var args = JsonDocument.Parse($"{{\"command\":\"echo allowed\"}}").RootElement;

        var result = await tool.ExecuteAsync(args, sandbox, CancellationToken.None);
        var bashResult = Assert.IsType<BashTool.BashResult>(result);

        Assert.Equal(0, bashResult.ExitCode);
        Assert.Contains("allowed", bashResult.Output);
    }
}
