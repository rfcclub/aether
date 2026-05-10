using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

/// <summary>
/// Tests for Aether.Tooling.ToolExecutor (registry-backed dispatch).
/// Covers tasks 2.6, 2.7, 2.8 from unify-tool-dispatch.
/// </summary>
public sealed class RegistryToolExecutorTests
{
    private static ToolRegistry CreateRegistry(params (string name, ToolDefinition def)[] tools)
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        foreach (var (name, def) in tools)
            registry.Register(name, def);
        return registry;
    }

    private static JsonElement Schema(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    private static ToolDefinition EchoTool() => new(
        "echo",
        "Echo the input",
        Schema("""{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}"""),
        (args, ct) => Task.FromResult<object>(args.GetProperty("text").GetString()!));

    private static ToolDefinition DisabledTool() => new(
        "secret",
        "Secret tool",
        Schema("""{"type":"object","properties":{}}"""),
        (_, _) => Task.FromResult<object>("hidden"),
        Enabled: false,
        DisabledReason: "secret tool disabled by policy");

    // ── 2.6: Unknown tool returns model-readable error ──

    [Fact]
    public async Task UnknownTool_ReturnsNotFound()
    {
        var registry = CreateRegistry(("echo", EchoTool()));
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("nope", "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("nope", result.Error!);
        Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledTool_ReturnsNotPermitted()
    {
        var registry = CreateRegistry(("echo", EchoTool()), ("secret", DisabledTool()));
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("secret", "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("disabled by policy", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2.7: Invalid JSON arguments fail gracefully ──

    [Fact]
    public async Task InvalidJson_ReturnsModelReadableError()
    {
        var registry = CreateRegistry(("echo", EchoTool()));
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("echo", "not json at all", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2.8: Missing required args fail with model-readable error ──

    [Fact]
    public async Task MissingRequiredArg_ReturnsModelReadableError()
    {
        var registry = CreateRegistry(("echo", EchoTool()));
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        // Call echo without "text" — the tool impl will throw on GetProperty
        var result = await executor.ExecuteAsync("echo", "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.NotEmpty(result.Error!);
        // Should not contain raw stack trace
        Assert.DoesNotContain("   at ", result.Error!);
    }

    [Fact]
    public async Task ValidArgs_ExecutesSuccessfully()
    {
        var registry = CreateRegistry(("echo", EchoTool()));
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("echo", """{"text":"hello"}""", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("hello", result.Data);
    }

    // ── Error messages do not leak stack traces ──

    [Fact]
    public async Task ErrorMessages_DoNotContainStackTrace()
    {
        var explodingTool = new ToolDefinition(
            "boom",
            "Explodes",
            Schema("""{"type":"object","properties":{}}"""),
            (_, _) => throw new InvalidOperationException("internal bug in Foo.Bar.Baz()"));
        var registry = CreateRegistry(("boom", explodingTool));
        var executor = new Aether.Tooling.ToolExecutor(registry, NullLogger<Aether.Tooling.ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync("boom", "{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.DoesNotContain("   at ", result.Error!);
        Assert.Contains("internal bug", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
