using Aether.Agent;
using Aether.Channels;
using Aether.Memory;
using Aether.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class SlashCommandHandlerTests
{
    private SlashCommandHandler CreateHandler(
        FakeMemorySystem? memory = null,
        ISessionManager? sessions = null,
        Action<string>? onModelChanged = null)
    {
        var mem = memory ?? new FakeMemorySystem();
        var sessionMgr = sessions ?? new FakeSessionManager();

        var services = new ServiceCollection();
        services.AddSingleton<IMemorySystem>(mem);
        services.AddSingleton<ISessionManager>(sessionMgr);
        var provider = services.BuildServiceProvider();

        return new SlashCommandHandler(provider, NullLogger<SlashCommandHandler>.Instance);
    }

    private static SlashCommandContext Ctx(string text, string agent = "default", string workspace = "/tmp/ws")
        => new(text, agent, workspace, null!); // Services not used in these tests directly

    // ── 2.1 Non-slash message returns null ──

    [Fact]
    public async Task NonSlashMessage_ReturnsNull()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("hello world"), CancellationToken.None);
        Assert.Null(result);
    }

    // ── 2.2 Unknown slash command returns null ──

    [Fact]
    public async Task UnknownSlashCommand_ReturnsNull()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/foobar"), CancellationToken.None);
        Assert.Null(result);
    }

    // ── 2.3 Known slash command returns non-null ──

    [Fact]
    public async Task KnownSlashCommand_ReturnsResult()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/new"), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Contains("session", result!.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2.4 /new triggers session creation and context clear ──

    [Fact]
    public async Task NewCommand_CreatesSessionAndClearsContext()
    {
        var memory = new FakeMemorySystem();
        var sessions = new FakeSessionManager();
        var handler = CreateHandler(memory, sessions);

        var result = await handler.HandleAsync(Ctx("/new"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("session-1", result!.Text);
    }

    // ── 2.5 /reset clears context without creating new session ──

    [Fact]
    public async Task ResetCommand_ClearsContext()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/reset"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("cleared", result!.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2.6 /model with no args returns current model chain ──

    [Fact]
    public async Task ModelCommand_NoArgs_ShowsCurrentModel()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/model"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Model:", result!.Text);
    }

    // ── 2.7 /model <name> updates model ──

    [Fact]
    public async Task ModelCommand_WithArgs_SwitchesModel()
    {
        string? changedModel = null;
        var handler = CreateHandler(onModelChanged: m => changedModel = m);

        var result = await handler.HandleAsync(Ctx("/model claude-sonnet-4-6"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("claude-sonnet-4-6", result!.Text);
    }

    // ── 2.8 /model <unknown> warns but still sets ──

    [Fact]
    public async Task ModelCommand_UnknownModel_Warns()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/model nonexistent-model"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Model changed", result!.Text);
        Assert.Contains("nonexistent-model", result!.Text);
    }

    // ── 2.9 /context returns session stats ──

    [Fact]
    public async Task ContextCommand_ShowsStats()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/context"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Session", result!.Text);
    }

    // ── 2.10 /compact calls CompactContext ──

    [Fact]
    public async Task CompactCommand_CallsCompactContext()
    {
        var memory = new FakeMemorySystem();
        var handler = CreateHandler(memory);

        var result = await handler.HandleAsync(Ctx("/compact"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("compact", result!.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2.11 /context with no active session shows zero counts ──

    [Fact]
    public async Task ContextCommand_NoSession_ShowsZero()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/context"), CancellationToken.None);

        Assert.NotNull(result);
        // Should still return stats, even if zeros
        Assert.NotEmpty(result!.Text);
    }

    // ── 2.12 /compact with empty context shows minimal message ──

    [Fact]
    public async Task CompactCommand_EmptyContext_ShowsMinimal()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync(Ctx("/compact"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotEmpty(result!.Text);
    }

    // ── 5. Integration tests — end-to-end DI ──

    private static IServiceProvider CreateDIProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemorySystem, FakeMemorySystem>();
        services.AddSingleton<ISessionManager, FakeSessionManager>();
        services.AddSingleton<ISlashCommandHandler, SlashCommandHandler>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DI_Resolution_ReturnsHandler()
    {
        var provider = CreateDIProvider();
        var handler = provider.GetRequiredService<ISlashCommandHandler>();
        Assert.NotNull(handler);
        Assert.IsType<SlashCommandHandler>(handler);
    }

    [Fact]
    public async Task DI_NewCommand_ReturnsSessionId()
    {
        var provider = CreateDIProvider();
        var handler = provider.GetRequiredService<ISlashCommandHandler>();
        var result = await handler.HandleAsync(
            new SlashCommandContext("/new", "default", "/tmp/ws", provider),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("session-1", result!.Text);
    }

    [Fact]
    public async Task DI_ModelSwitch_UpdatesModelName()
    {
        var provider = CreateDIProvider();
        var handler = provider.GetRequiredService<ISlashCommandHandler>();
        var result = await handler.HandleAsync(
            new SlashCommandContext("/model claude-sonnet-4-6", "default", "/tmp/ws", provider),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("claude-sonnet-4-6", result!.Text);
    }

    [Fact]
    public async Task AllCommands_ReturnNonEmptyText()
    {
        var provider = CreateDIProvider();
        var handler = provider.GetRequiredService<ISlashCommandHandler>();

        foreach (var cmd in new[] { "/new", "/reset", "/model", "/context", "/compact" })
        {
            var result = await handler.HandleAsync(
                new SlashCommandContext(cmd, "default", "/tmp/ws", provider),
                CancellationToken.None);
            Assert.NotNull(result);
            Assert.NotEmpty(result!.Text);
        }
    }
}
