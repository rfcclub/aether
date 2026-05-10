using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

/// <summary>
/// Tests for compatibility aliases (tasks 3.3, 3.4 from unify-tool-dispatch).
/// </summary>
public sealed class ShellAliasTests
{
    private static JsonElement Schema(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static ToolRegistry CreateRegistryWithAliases()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var bashSchema = Schema("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""");

        // Register real bash tool
        registry.Register("bash", new ToolDefinition(
            "bash", "Execute shell command", bashSchema,
            (args, ct) => Task.FromResult<object>($"bash:{args.GetProperty("command").GetString()}"),
            ToolRisk.Exec));

        // Register shell alias (same implementation)
        registry.Register("shell", new ToolDefinition(
            "shell", "Compatibility alias for bash.", bashSchema,
            (args, ct) => Task.FromResult<object>($"bash:{args.GetProperty("command").GetString()}"),
            ToolRisk.Exec));

        // Register exec alias (disabled)
        registry.Register("exec", new ToolDefinition(
            "exec", "Compatibility alias for bash, disabled by default.", bashSchema,
            (_, _) => Task.FromResult<object>("exec alias disabled by policy."),
            ToolRisk.Exec, Enabled: false,
            DisabledReason: "exec alias is disabled by default; use bash/shell or enable it explicitly."));

        return registry;
    }

    [Fact]
    public async Task Shell_ProducesSameOutputAsBash()
    {
        var registry = CreateRegistryWithAliases();
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);
        var args = """{"command":"echo hello"}""";

        var bashResult = await executor.ExecuteAsync("bash", args, CancellationToken.None);
        var shellResult = await executor.ExecuteAsync("shell", args, CancellationToken.None);

        Assert.True(bashResult.Success);
        Assert.True(shellResult.Success);
        Assert.Equal(bashResult.Data, shellResult.Data);
    }

    [Fact]
    public async Task Shell_IsRegisteredAndEnabled()
    {
        var registry = CreateRegistryWithAliases();
        var shell = registry.Resolve("shell");

        Assert.NotNull(shell);
        Assert.True(shell!.Enabled);
        Assert.Equal("shell", shell.Name);
    }

    [Fact]
    public async Task Exec_IsDisabledByDefault()
    {
        var registry = CreateRegistryWithAliases();
        var exec = registry.Resolve("exec");

        Assert.NotNull(exec);
        Assert.False(exec!.Enabled);
        Assert.NotNull(exec.DisabledReason);
        Assert.Contains("disabled", exec.DisabledReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exec_Dispatch_RejectsWithPolicyReason()
    {
        var registry = CreateRegistryWithAliases();
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("exec", """{"command":"echo hi"}""", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("disabled", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
