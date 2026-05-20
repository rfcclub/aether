using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class ToolSafetyTests
{
    private readonly string _workspace;
    private readonly SandboxContext _sandbox;

    public ToolSafetyTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "aether_safety_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspace);
        _sandbox = new SandboxContext(_workspace);
    }

    [Fact]
    public async Task ReadTool_PathTraversal_Blocked()
    {
        var tool = new ReadTool(NullLogger<ReadTool>.Instance);
        // Try to go up from workspace to /etc/passwd (simulated)
        var args = JsonDocument.Parse($"{{\"path\":\"../../../../etc/passwd\"}}").RootElement;
        
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, _sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task WriteTool_PathTraversal_Blocked()
    {
        var tool = new WriteTool(NullLogger<WriteTool>.Instance);
        var args = JsonDocument.Parse($"{{\"path\":\"../outside.txt\",\"content\":\"evil\"}}").RootElement;
        
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, _sandbox, CancellationToken.None));
    }

    [Fact]
    public async Task BashTool_RmRfRoot_BlockedByCommandFiltering()
    {
        var tool = new BashTool(NullLogger<BashTool>.Instance);
        // Only allow safe commands
        var sandbox = new SandboxContext(_workspace, new Aether.Config.SpecToolsSection
        {
            Shell = new Aether.Config.SpecShellTool { AllowedCommands = new List<string> { "ls", "echo" } }
        });

        var args = JsonDocument.Parse($"{{\"command\":\"rm -rf /\"}}").RootElement;

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => tool.ExecuteAsync(args, sandbox, CancellationToken.None));
        Assert.Contains("Command 'rm' not permitted", ex.Message);
    }
}
