using System.CommandLine;
using System.CommandLine.IO;
using Aether.Cli;
using Aether.Config;
using Aether.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class AgentCliTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;
    private readonly AgentAuthProfiles _authProfiles;
    private readonly AgentWorkspaceScaffolder _scaffolder;

    public AgentCliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_cli_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
        _authProfiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        _scaffolder = new AgentWorkspaceScaffolder(NullLogger<AgentWorkspaceScaffolder>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Agent_add_command_parses()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        var result = await cmd.InvokeAsync("agent add testagent --non-interactive", console);

        Assert.Equal(0, result);
        var agentDir = Path.Combine(_aetherDir, "workspaces", "testagent");
        Assert.True(Directory.Exists(agentDir));
    }

    [Fact]
    public async Task Agent_add_requires_name()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        var result = await cmd.InvokeAsync("agent add", console);

        Assert.NotEqual(0, result);
    }

    [Fact]
    public async Task Agent_add_creates_auth_profile()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);

        var auth = await _authProfiles.LoadAuthProfilesAsync("testagent");
        Assert.Null(auth.State.ActiveProvider);
        Assert.Null(auth.State.ActiveModel);
    }

    [Fact]
    public async Task Agent_add_with_model_flag()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --model claude-opus-4-7 --non-interactive", console);

        var auth = await _authProfiles.LoadAuthProfilesAsync("testagent");
        Assert.Equal("claude-opus-4-7", auth.State.ActiveModel);
    }

    [Fact]
    public async Task Agent_list_shows_output()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        // Add an agent first
        await cmd.InvokeAsync("agent add testagent --non-interactive", console);

        console = new TestConsole();
        var result = await cmd.InvokeAsync("agent list", console);
        Assert.Equal(0, result);
        Assert.Contains("testagent", console.Out.ToString()!);
    }

    [Fact]
    public async Task Agent_delete_removes_agent()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);

        console = new TestConsole();
        var result = await cmd.InvokeAsync("agent delete testagent --force", console);
        Assert.Equal(0, result);

        // Without --prune-workspace, workspace stays on disk
    }

    [Fact]
    public async Task Agent_delete_prune_workspace()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);
        Assert.True(Directory.Exists(Path.Combine(_aetherDir, "workspaces", "testagent")));

        console = new TestConsole();
        await cmd.InvokeAsync("agent delete testagent --prune-workspace --force", console);

        Assert.False(Directory.Exists(Path.Combine(_aetherDir, "workspaces", "testagent")));
        Assert.False(Directory.Exists(Path.Combine(_aetherDir, "agents", "testagent")));
    }

    [Fact]
    public async Task Agent_set_identity_updates_config()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);

        console = new TestConsole();
        var result = await cmd.InvokeAsync("agent set-identity testagent --display-name \"Test Agent\" --emoji robot", console);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Agent_bind_adds_channel_binding()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);

        console = new TestConsole();
        var result = await cmd.InvokeAsync("agent bind testagent --channel telegram:12345", console);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Agent_unbind_removes_channel_binding()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);
        await cmd.InvokeAsync("agent bind testagent --channel telegram:12345", console);

        console = new TestConsole();
        var result = await cmd.InvokeAsync("agent unbind testagent --channel telegram:12345", console);
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Agent_bind_list_shows_bindings()
    {
        var cli = new AetherCli(_aetherDir, _scaffolder, _authProfiles, NullLogger<AetherCli>.Instance);
        var console = new TestConsole();
        var cmd = cli.BuildRootCommand();

        await cmd.InvokeAsync("agent add testagent --non-interactive", console);
        await cmd.InvokeAsync("agent bind testagent --channel telegram:12345", console);

        console = new TestConsole();
        var result = await cmd.InvokeAsync("agent bind testagent", console);
        Assert.Equal(0, result);
        Assert.Contains("telegram:12345", console.Out.ToString()!);
    }
}
