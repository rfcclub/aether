using Aether.Providers;
using Aether.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class ProviderRouterTests
{
    private static AetherDb CreateTempDb()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid():N}.db");
        var schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aether", "Data", "Schema.sql"));
        if (!File.Exists(schemaPath))
        {
            schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql"));
        }
        return new AetherDb(dbPath, schemaPath);
    }

    [Fact]
    public async Task CompleteAsync_PrimaryProviderSucceeds_UsesPrimary()
    {
        var primary = new FakeLlmProvider("fireworks", model: "gpt-4", response: new LlmResponse("primary response"));
        var fallback = new FakeLlmProvider("openrouter", model: "claude", response: new LlmResponse("fallback response"));
        using var db = CreateTempDb();
        var router = new ProviderRouter(
            new ILLMProvider[] { primary, fallback },
            new ProviderRoutingOptions
            {
                ProviderPriorities = new Dictionary<string, int> { ["fireworks"] = 1, ["openrouter"] = 2 }
            },
            db,
            NullLogger<ProviderRouter>.Instance);

        var response = await router.CompleteAsync(
            new LlmRequest(new[] { LlmMessage.User("hello") }),
            CancellationToken.None);

        Assert.Equal("primary response", response.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_PrimaryFails_FallsBackToSecondary()
    {
        var primary = new FakeLlmProvider("fireworks", model: "gpt-4", throwOnCall: true);
        var fallback = new FakeLlmProvider("openrouter", model: "claude", response: new LlmResponse("fallback response"));
        using var db = CreateTempDb();
        var router = new ProviderRouter(
            new ILLMProvider[] { primary, fallback },
            new ProviderRoutingOptions
            {
                ComplexityThreshold = 0.0f, // Always escalate
                ProviderPriorities = new Dictionary<string, int> { ["fireworks"] = 1, ["openrouter"] = 2 }
            },
            db,
            NullLogger<ProviderRouter>.Instance);

        var response = await router.CompleteAsync(
            new LlmRequest(new[] { LlmMessage.User("architecture design review") }),
            CancellationToken.None);

        Assert.Equal("fallback response", response.Content);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_AllFailed_Throws()
    {
        var primary = new FakeLlmProvider("fireworks", model: "gpt-4", throwOnCall: true);
        var fallback = new FakeLlmProvider("openrouter", model: "claude", throwOnCall: true);
        using var db = CreateTempDb();
        var router = new ProviderRouter(
            new ILLMProvider[] { primary, fallback },
            new ProviderRoutingOptions
            {
                ComplexityThreshold = 0.0f,
                ProviderPriorities = new Dictionary<string, int> { ["fireworks"] = 1, ["openrouter"] = 2 }
            },
            db,
            NullLogger<ProviderRouter>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.CompleteAsync(
                new LlmRequest(new[] { LlmMessage.User("architecture design") }),
                CancellationToken.None));
    }

    [Fact]
    public void Name_ReturnsRouter()
    {
        using var db = CreateTempDb();
        var router = new ProviderRouter(
            Array.Empty<ILLMProvider>(),
            new ProviderRoutingOptions(),
            db,
            NullLogger<ProviderRouter>.Instance);

        Assert.Equal("Router", router.Name);
        Assert.Equal("Multi", router.Model);
    }

    [Fact]
    public async Task HealthCheck_AnyHealthy_ReturnsTrue()
    {
        var healthy = new FakeLlmProvider("healthy", model: "test", response: new LlmResponse("ok"));
        var unhealthy = new FakeLlmProvider("unhealthy", model: "test", throwOnHealthCheck: true);
        using var db = CreateTempDb();
        var router = new ProviderRouter(
            new ILLMProvider[] { healthy, unhealthy },
            new ProviderRoutingOptions(),
            db,
            NullLogger<ProviderRouter>.Instance);

        var result = await router.HealthCheckAsync();
        Assert.True(result);
    }
}
