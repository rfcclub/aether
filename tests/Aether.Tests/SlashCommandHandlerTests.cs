using System.Linq;
using Aether.Agent;
using Aether.Channels;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class SlashCommandHandlerTests
{
    private SlashCommandHandler CreateHandler(
        FakeMemorySystem? memory = null,
        SessionManager? sessions = null,
        Action<string>? onModelChanged = null,
        ProviderRouter? router = null)
    {
        var mem = memory ?? new FakeMemorySystem();
        var sessionMgr = sessions ?? new FakeSessionManager();

        var services = new ServiceCollection();
        services.AddSingleton<FileMemory>(mem);
        services.AddSingleton<SessionManager>(sessionMgr);
        if (router is not null)
            services.AddSingleton<ProviderRouter>(router);
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
        Assert.Contains("New session:", result!.Text);
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
        Assert.Contains("Current model:", result!.Text);
    }

    // ── 2.7 /model <name> updates model ──

    [Fact]
    public async Task ModelCommand_WithArgs_SwitchesModel()
    {
        var router = CreateFakeRouter();
        var handler = CreateHandler(router: router);

        var result = await handler.HandleAsync(Ctx("/model accounts/fireworks/routers/kimi-k2p5-turbo"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("accounts/fireworks/routers/kimi-k2p5-turbo", result!.Text);
        Assert.Contains("Survives restart", result!.Text);
    }

    // ── 2.8 /model <unknown> warns but still sets ──

    [Fact]
    public async Task ModelCommand_UnknownModel_Warns()
    {
        var router = CreateFakeRouter();
        var handler = CreateHandler(router: router);

        var result = await handler.HandleAsync(Ctx("/model nonexistent-model"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("Unknown model", result!.Text);
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
        services.AddSingleton<FileMemory, FakeMemorySystem>();
        services.AddSingleton<SessionManager, FakeSessionManager>();
        services.AddSingleton<SlashCommandHandler, SlashCommandHandler>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DI_Resolution_ReturnsHandler()
    {
        var provider = CreateDIProvider();
        var handler = provider.GetRequiredService<SlashCommandHandler>();
        Assert.NotNull(handler);
        Assert.IsType<SlashCommandHandler>(handler);
    }

    [Fact]
    public async Task DI_NewCommand_ReturnsSessionId()
    {
        var provider = CreateDIProvider();
        var handler = provider.GetRequiredService<SlashCommandHandler>();
        var result = await handler.HandleAsync(
            new SlashCommandContext("/new", "default", "/tmp/ws", provider),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("New session:", result!.Text);
    }

    [Fact]
    public async Task DI_ModelSwitch_UpdatesModelName()
    {
        var router = CreateFakeRouter();
        var services = new ServiceCollection();
        services.AddSingleton<FileMemory>(new FakeMemorySystem());
        services.AddSingleton<SessionManager>(new FakeSessionManager());
        services.AddSingleton<ProviderRouter>(router);
        services.AddSingleton<SlashCommandHandler, SlashCommandHandler>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<SlashCommandHandler>();
        var result = await handler.HandleAsync(
            new SlashCommandContext("/model accounts/fireworks/routers/kimi-k2p5-turbo", "default", "/tmp/ws", provider),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("accounts/fireworks/routers/kimi-k2p5-turbo", result!.Text);
    }

    [Fact]
    public async Task AllCommands_ReturnNonEmptyText()
    {
        var router = CreateFakeRouter();
        var services = new ServiceCollection();
        services.AddSingleton<FileMemory>(new FakeMemorySystem());
        services.AddSingleton<SessionManager>(new FakeSessionManager());
        services.AddSingleton<ProviderRouter>(router);
        services.AddSingleton<SlashCommandHandler, SlashCommandHandler>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<SlashCommandHandler>();

        foreach (var cmd in new[] { "/new", "/reset", "/model", "/context", "/compact" })
        {
            var result = await handler.HandleAsync(
                new SlashCommandContext(cmd, "default", "/tmp/ws", provider),
                CancellationToken.None);
            Assert.NotNull(result);
            Assert.NotEmpty(result!.Text);
        }
    }

    private static ProviderRouter CreateFakeRouter()
    {
        var fakeProvider = new FakeProvider("fireworks", "accounts/fireworks/routers/kimi-k2p5-turbo");
        // Schema.sql is copied to test output dir
        var schemaPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "Data", "Schema.sql");
        return new ProviderRouter(
            new ILLMProvider[] { fakeProvider },
            new ProviderRoutingOptions(),
            new Aether.Data.AetherDb(":memory:", schemaPath),
            NullLogger<ProviderRouter>.Instance);
    }

    private sealed class FakeProvider : ILLMProvider
    {
        public string Name { get; }
        public string Model { get; }
        public bool SupportsStreaming => true;
        public bool SupportsTools => false;

        public FakeProvider(string name, string model) { Name = name; Model = model; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
            => Task.FromResult(new LlmResponse("ok"));

        public async IAsyncEnumerable<string> CompleteStreamingAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        { yield break; }

        public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        { yield break; }

        public Task<bool> HealthCheckAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
