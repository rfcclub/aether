using Aether.Config;
using Aether.Tooling;

namespace Aether.Tests;

public sealed class SandboxContextTests
{
    private readonly string _workspace = "/tmp/test_ws";

    [Fact]
    public void PathInWorkspace_Allowed()
    {
        var sandbox = new SandboxContext(_workspace);

        Assert.True(sandbox.IsPathAllowed("/tmp/test_ws/file.txt"));
        Assert.True(sandbox.IsPathAllowed("/tmp/test_ws/subdir/file.txt"));
    }

    [Fact]
    public void PathOutsideWorkspace_AllowedByDefault()
    {
        var sandbox = new SandboxContext(_workspace);

        // IsPathAllowed defaults to true — only denies if in DeniedPaths
        Assert.True(sandbox.IsPathAllowed("/etc/passwd"));
        Assert.True(sandbox.IsPathAllowed("/home/user/secret.txt"));
    }

    [Fact]
    public void PathInDeniedList_Denied()
    {
        var sandbox = new SandboxContext(_workspace, new SpecToolsSection
        {
            File = new SpecFileTool
            {
                AllowedPaths = new List<string> { "/tmp/test_ws" },
                DeniedPaths = new List<string> { "/tmp/test_ws/secrets" }
            }
        });

        Assert.True(sandbox.IsPathAllowed("/tmp/test_ws/file.txt"));
        Assert.False(sandbox.IsPathAllowed("/tmp/test_ws/secrets/key.pem"));
    }

    [Fact]
    public void ExplicitAllowedPath_Allowed()
    {
        var sandbox = new SandboxContext(_workspace, new SpecToolsSection
        {
            File = new SpecFileTool
            {
                AllowedPaths = new List<string> { "/data", "/tmp/test_ws" },
                DeniedPaths = new List<string> { "/etc" }
            }
        });

        Assert.True(sandbox.IsPathAllowed("/data/foo.txt"));
        Assert.True(sandbox.IsPathAllowed("/tmp/test_ws/bar.txt"));
        Assert.False(sandbox.IsPathAllowed("/etc/passwd"));
    }

    [Fact]
    public void DefaultConfig_HasCorrectDefaults()
    {
        var sandbox = new SandboxContext("/ws");

        Assert.Equal("/ws", sandbox.WorkspacePath);
        Assert.True(sandbox.AllowWrites);
        Assert.Single(sandbox.AllowedPaths);
        Assert.Equal("/ws", sandbox.AllowedPaths[0]);
        Assert.Empty(sandbox.DeniedPaths);
        Assert.Empty(sandbox.AllowedCommands);
        Assert.Equal(60, sandbox.BashTimeoutSeconds);
    }

    [Fact]
    public void ShellConfig_OverridesDefaults()
    {
        var sandbox = new SandboxContext("/ws", new SpecToolsSection
        {
            Shell = new SpecShellTool
            {
                AllowedCommands = new List<string> { "ls", "cat" },
                TimeoutSeconds = 30,
            }
        });

        Assert.Equal(30, sandbox.BashTimeoutSeconds);
        Assert.Equal(2, sandbox.AllowedCommands.Count);
    }
}
