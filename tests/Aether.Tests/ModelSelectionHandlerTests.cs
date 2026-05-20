using Aether.Config;
using Aether.Providers;
using Aether.Ui;
using Aether.Ui.Handlers;
using Aether.Ui.Renderers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace Aether.Tests;

public sealed class ModelSelectionHandlerTests
{
    private static (ModelSelectionHandler, ProviderRouter) CreateHandler(
        IReadOnlyList<ILLMProvider>? providersOverride = null)
    {
        var providers = providersOverride ?? new ILLMProvider[]
        {
            new FakeProvider("fireworks", "fireworks/kimi-k2p6"),
            new FakeProvider("openrouter", "openrouter/claude-sonnet"),
            new FakeProvider("anthropic", "anthropic/claude-opus"),
        };

        var dbSchema = Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql");
        var router = new ProviderRouter(providers, new ProviderRoutingOptions(),
            new Aether.Data.AetherDb(":memory:", dbSchema), NullLogger<ProviderRouter>.Instance);

        var handler = new ModelSelectionHandler();
        return (handler, router);
    }

    private static IServiceProvider CreateServices(ProviderRouter router)
    {
        var services = new ServiceCollection();
        services.AddSingleton(router);
        services.AddSingleton<ConfigLoader>(_ => null!); // not used in browse/list
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Browse_ReturnsProviderList()
    {
        var (handler, router) = CreateHandler();
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "browse" }, services, "test");

        Assert.NotNull(doc);
        Assert.Contains("fireworks", doc!.Sections.SelectMany(s => s.Items).Select(i => i.Label));
        Assert.Contains("openrouter", doc.Sections.SelectMany(s => s.Items).Select(i => i.Label));
        Assert.Contains("anthropic", doc.Sections.SelectMany(s => s.Items).Select(i => i.Label));
    }

    [Fact]
    public async Task Browse_ShowsCurrentModel()
    {
        var (handler, router) = CreateHandler();
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "browse" }, services, "test");

        Assert.NotNull(doc);
        var fireworksItem = doc!.Sections
            .SelectMany(s => s.Items)
            .First(i => i.Label == "fireworks");
        Assert.True(fireworksItem.Selected); // kimi is current (first provider's model)
    }

    [Fact]
    public async Task List_ShowsModelsForProvider()
    {
        var (handler, router) = CreateHandler();
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "list", Data = "fireworks" }, services, "test");

        Assert.NotNull(doc);
        Assert.Contains("fireworks", doc!.Text);
        // Should have a Back button
        var allItems = doc.Sections.SelectMany(s => s.Items);
        Assert.Contains(allItems, i => i.Id == "browse");
    }

    [Fact]
    public async Task List_WithManyModels_RendersOnlyCurrentPage()
    {
        var providers = Enumerable.Range(0, 20)
            .Select(i => new FakeProvider($"openrouter/vendor/model-{i:D2}", $"vendor/model-{i:D2}"))
            .Cast<ILLMProvider>()
            .ToList();
        var (handler, router) = CreateHandler(providers);
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "list", Data = "openrouter" },
            services,
            "test");

        Assert.NotNull(doc);
        Assert.Single(doc!.Sections);
        Assert.Equal(UiLayout.Paged, doc.Layout);
        Assert.Equal(0, doc.PageIndex);
        Assert.Equal(3, doc.TotalPages);
        Assert.Contains("page 1/3", doc.Sections[0].Title);
        Assert.Contains(doc.Sections[0].Items, i => i.Id == "browse");
        Assert.Contains(doc.Sections[0].Items, i => i.Id == "selectat:openrouter:0");
        Assert.DoesNotContain(doc.Sections[0].Items, i => i.Label == "model-08");

        var (_, markup) = ((string, InlineKeyboardMarkup))new TelegramUiRenderer().Render(doc);
        var pagination = markup.InlineKeyboard.Last();
        Assert.Equal("·", pagination.ElementAt(0).Text);
        Assert.Equal("Page 1/3", pagination.ElementAt(1).Text);
        Assert.Equal("▶️", pagination.ElementAt(2).Text);
        Assert.Equal("model:page:openrouter:1", pagination.ElementAt(2).CallbackData);
    }

    [Fact]
    public async Task Page_ShowsRequestedModelPage()
    {
        var providers = Enumerable.Range(0, 20)
            .Select(i => new FakeProvider($"openrouter/vendor/model-{i:D2}", $"vendor/model-{i:D2}"))
            .Cast<ILLMProvider>()
            .ToList();
        var (handler, router) = CreateHandler(providers);
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "page", Data = "openrouter:1" },
            services,
            "test");

        Assert.NotNull(doc);
        Assert.Equal(UiLayout.Paged, doc!.Layout);
        Assert.Equal(1, doc.PageIndex);
        Assert.Equal(3, doc.TotalPages);
        Assert.Contains("page 2/3", doc!.Sections[0].Title);
        Assert.Contains(doc.Sections[0].Items, i => i.Label == "model-08");
        Assert.DoesNotContain(doc.Sections[0].Items, i => i.Label == "model-00");

        var (_, markup) = ((string, InlineKeyboardMarkup))new TelegramUiRenderer().Render(doc);
        var pagination = markup.InlineKeyboard.Last();
        Assert.Equal("◀️", pagination.ElementAt(0).Text);
        Assert.Equal("model:page:openrouter:0", pagination.ElementAt(0).CallbackData);
        Assert.Equal("Page 2/3", pagination.ElementAt(1).Text);
        Assert.Equal("▶️", pagination.ElementAt(2).Text);
        Assert.Equal("model:page:openrouter:2", pagination.ElementAt(2).CallbackData);
    }

    [Fact]
    public async Task Select_UnknownModel_ReturnsError()
    {
        var (handler, router) = CreateHandler();
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "select", Data = "nonexistent/model" },
            services, "test");

        Assert.NotNull(doc);
        Assert.Contains("Unknown model", doc!.Text);
    }

    // Reset and select-with-persistence tests require ConfigLoader integration.
    // These are tested at the SlashCommandHandler level where ConfigLoader is available.

    [Fact]
    public async Task UnknownAction_ReturnsNull()
    {
        var (handler, router) = CreateHandler();
        var services = CreateServices(router);

        var doc = await handler.HandleAsync(
            new UiCallback { Namespace = "model", Action = "unknown_action" }, services, "test");

        Assert.Null(doc);
    }

    [Fact]
    public void Namespace_IsModel()
    {
        var handler = new ModelSelectionHandler();
        Assert.Equal("model", handler.Namespace);
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

        public async IAsyncEnumerable<string> CompleteStreamingAsync(LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        { yield break; }

        public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(LlmRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        { yield break; }

        public Task<bool> HealthCheckAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
