using Aether;
using Aether.Config;
using Aether.Data;
using Aether.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class ProviderRouterModelRoutingTests : IDisposable
{
    private readonly string _dbPath;

    public ProviderRouterModelRoutingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether-mr-{Guid.NewGuid():N}.db");
        var db = new AetherDb(_dbPath, FindSchemaPath());
        db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static string FindSchemaPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aether", "Data", "Schema.sql"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("Cannot find Schema.sql");
    }

    private ProviderRouter CreateRouter(
        IEnumerable<ILLMProvider> providers,
        IReadOnlyList<string>? modelChain = null,
        AgentSpecConfig? agentSpec = null)
    {
        var db = new AetherDb(_dbPath, FindSchemaPath());
        var logger = NullLogger<ProviderRouter>.Instance;
        var options = new ProviderRoutingOptions();
        var router = new ProviderRouter(providers.ToList(), options, db, logger);
        router.ModelChain = modelChain;
        router.CurrentAgent = agentSpec;
        return router;
    }

    private static LlmRequest SimpleRequest => new(Messages: [new LlmMessage("user", "hello")]);

    // ── 4.1 Agent with primary + fallback models uses primary first ──

    [Fact]
    public async Task ModelChain_UsesPrimary_WhenPrimarySucceeds()
    {
        var primary = new FakeLlmProvider("openrouter", "deepseek/deepseek-r1",
            new LlmResponse("primary response"));
        var fallback = new FakeLlmProvider("anthropic", "claude-sonnet-4-6",
            new LlmResponse("fallback response"));

        var router = CreateRouter([primary, fallback],
            modelChain: ["deepseek/deepseek-r1", "claude-sonnet-4-6"],
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new() { Type = "openrouter", Model = "deepseek/deepseek-r1" },
                    ["anthropic"] = new() { Type = "anthropic", Model = "claude-sonnet-4-6" }
                }
            });

        var result = await router.CompleteAsync(SimpleRequest);

        Assert.Equal("primary response", result.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    // ── 4.2 Agent falls back to second model when primary fails ──

    [Fact]
    public async Task ModelChain_FallsBack_WhenPrimaryFails()
    {
        var primary = new FakeLlmProvider("openrouter", "deepseek/deepseek-r1",
            throwOnCall: true);
        var fallback = new FakeLlmProvider("anthropic", "claude-sonnet-4-6",
            new LlmResponse("fallback wins"));

        var router = CreateRouter([primary, fallback],
            modelChain: ["deepseek/deepseek-r1", "claude-sonnet-4-6"],
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new() { Type = "openrouter", Model = "deepseek/deepseek-r1" },
                    ["anthropic"] = new() { Type = "anthropic", Model = "claude-sonnet-4-6" }
                }
            });

        var result = await router.CompleteAsync(SimpleRequest);

        Assert.Equal("fallback wins", result.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    // ── 4.3 Agent falls back through all models in chain ──

    [Fact]
    public async Task ModelChain_IteratesAll_UntilSuccess()
    {
        var first = new FakeLlmProvider("openrouter", "model-1", throwOnCall: true);
        var second = new FakeLlmProvider("fireworks", "model-2", throwOnCall: true);
        var third = new FakeLlmProvider("anthropic", "model-3", new LlmResponse("third time lucky"));

        var router = CreateRouter([first, second, third],
            modelChain: ["model-1", "model-2", "model-3"],
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new() { Type = "openrouter", Model = "model-1" },
                    ["fireworks"] = new() { Type = "openai", Model = "model-2" },
                    ["anthropic"] = new() { Type = "anthropic", Model = "model-3" }
                }
            });

        var result = await router.CompleteAsync(SimpleRequest);

        Assert.Equal("third time lucky", result.Content);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(1, third.CallCount);
    }

    // ── 4.4 Model resolved by prefix match ──

    [Fact]
    public void ResolveModelToProvider_MatchesByPrefix()
    {
        var openrouter = new FakeLlmProvider("openrouter", "deepseek/deepseek-r1");
        var router = CreateRouter([openrouter],
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new() { Type = "openrouter", Model = "deepseek/deepseek-r1" }
                }
            });

        var resolved = router.ResolveModelToProvider("openrouter/anthropic/claude-sonnet-4-6");

        Assert.NotNull(resolved);
        Assert.Equal("openrouter", resolved.Name);
    }

    // ── 4.5 Model resolved by explicit Models list ──

    [Fact]
    public void ResolveModelToProvider_MatchesByModelsList()
    {
        var openrouter = new FakeLlmProvider("openrouter", "deepseek/deepseek-r1");
        var router = CreateRouter([openrouter],
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new()
                    {
                        Type = "openrouter",
                        Model = "deepseek/deepseek-r1",
                        Models = new List<string> { "deepseek/deepseek-r1", "nvidia/nemotron-3-super", "qwen/qwq-32b" }
                    }
                }
            });

        var resolved = router.ResolveModelToProvider("qwen/qwq-32b");

        Assert.NotNull(resolved);
        Assert.Equal("openrouter", resolved.Name);
    }

    // ── 4.6 Unresolvable model is skipped with warning ──

    [Fact]
    public async Task ModelChain_SkipsUnresolvableModel()
    {
        var valid = new FakeLlmProvider("openrouter", "deepseek/deepseek-r1",
            new LlmResponse("finally"));

        var router = CreateRouter([valid],
            modelChain: ["unknown-model-xyz", "deepseek/deepseek-r1"],
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new() { Type = "openrouter", Model = "deepseek/deepseek-r1" }
                }
            });

        var result = await router.CompleteAsync(SimpleRequest);

        Assert.Equal("finally", result.Content);
        Assert.Equal(1, valid.CallCount);
    }

    // ── Backward compat: no ModelChain falls back to provider priorities ──

    [Fact]
    public async Task NoModelChain_UsesProviderPriorityRouting()
    {
        var primary = new FakeLlmProvider("fireworks", "fw-model",
            new LlmResponse("fw response"));
        var fallback = new FakeLlmProvider("openrouter", "or-model",
            new LlmResponse("or response"));

        var router = CreateRouter([primary, fallback], modelChain: null);

        var result = await router.CompleteAsync(SimpleRequest);

        Assert.NotNull(result);
    }


    // ── Provider-slug resolution (fireworks-ai/... → fireworks) ──

    [Fact]
    public void ResolveModelToProvider_HyphenSlug_ResolvesToProvider()
    {
        var fireworks = new FakeLlmProvider("fireworks", "accounts/fireworks/routers/kimi-k2p6-turbo");
        var router = CreateRouter(new[] { fireworks });

        var result = router.ResolveModelToProvider("fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo");

        Assert.NotNull(result);
        Assert.Equal("fireworks", result!.Name);
    }

    [Fact]
    public void ResolveModelToProvider_ExactPrefix_TakesPrecedenceOverSlug()
    {
        var openrouter = new FakeLlmProvider("openrouter", "some-model");
        var router = CreateRouter(new[] { openrouter });

        var result = router.ResolveModelToProvider("openrouter/deepseek/deepseek-r1:free");

        Assert.NotNull(result);
        Assert.Equal("openrouter", result!.Name);
    }

    [Fact]
    public void ResolveModelToProvider_ExactPrefix_MatchesExpandedProviderName()
    {
        var openrouter = new FakeLlmProvider(
            "openrouter/nvidia/nemotron-3-super-120b-a12b:free",
            "nvidia/nemotron-3-super-120b-a12b:free");
        var router = CreateRouter(new[] { openrouter });

        var result = router.ResolveModelToProvider("openrouter/deepseek/deepseek-r1:free");

        Assert.NotNull(result);
        Assert.Equal("openrouter/nvidia/nemotron-3-super-120b-a12b:free", result!.Name);
    }

    [Fact]
    public void ResolveModelToProvider_UnknownSlug_ReturnsNull()
    {
        var fireworks = new FakeLlmProvider("fireworks", "model");
        var router = CreateRouter(new[] { fireworks });

        var result = router.ResolveModelToProvider("unknown-slug/some-model");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveModelToProvider_AILStripped()
    {
        // "google-ai/model" → strip "-ai" → "google"
        var google = new FakeLlmProvider("google", "gemini-2.5-flash");
        var router = CreateRouter(new[] { google });

        var result = router.ResolveModelToProvider("google-ai/gemini-2.5-flash");

        Assert.NotNull(result);
        Assert.Equal("google", result!.Name);
    }

    [Fact]
    public async Task ModelChain_FallsBackThroughFullChain()
    {
        var primary = new FakeLlmProvider("fireworks", "accounts/fireworks/routers/kimi", throwOnCall: true);
        var fallback = new FakeLlmProvider("openrouter", "deepseek-r1");

        var router = CreateRouter(
            new ILLMProvider[] { primary, fallback },
            modelChain: new[] { "fireworks-ai/accounts/fireworks/routers/kimi", "openrouter/deepseek/deepseek-r1:free" });

        var response = await router.CompleteAsync(SimpleRequest, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("deepseek-r1", fallback.Model);
    }

    [Fact]
    public async Task ModelChain_Streaming_FallsBack_WhenPrimaryFails()
    {
        var primary = new FakeLlmProvider("openrouter", "deepseek/deepseek-r1", throwOnCall: true);
        var fallback = new FakeStreamingProvider("fallback stream response", chunks: 2);

        var router = CreateRouter(
            new ILLMProvider[] { primary, fallback },
            modelChain: new[] { "deepseek/deepseek-r1", "stream-model" },
            agentSpec: new AgentSpecConfig
            {
                Providers = new Dictionary<string, SpecProviderEntry>
                {
                    ["openrouter"] = new() { Type = "openrouter", Model = "deepseek/deepseek-r1" },
                    ["fake-streaming"] = new() { Type = "fake-streaming", Model = "stream-model" }
                }
            });

        var eventList = new List<StreamEvent>();
        await foreach (var evt in router.CompleteStreamingEventsAsync(SimpleRequest, CancellationToken.None))
        {
            eventList.Add(evt);
        }

        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);

        // Verify tokens were received
        var tokens = eventList.OfType<StreamEvent.TextToken>().Select(t => t.Token).ToList();
        var fullText = string.Join("", tokens);
        Assert.Contains("fallback stream response", fullText);

        var responseEvt = eventList.OfType<StreamEvent.Response>().FirstOrDefault();
        Assert.NotNull(responseEvt);
        Assert.Equal("fallback stream response", responseEvt.LlmResponse.Content);
    }

}

