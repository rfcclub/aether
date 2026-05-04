using System.Text.Json;
using Aether.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Tests;

public sealed class ToolInterfacesTests
{
    [Fact]
    public void IToolImplementation_ResolvesFromDI()
    {
        var services = new ServiceCollection();
        var impl = new FakeToolImpl("test_tool", "A test tool");
        services.AddSingleton<IToolImplementation>(impl);
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<IToolImplementation>();
        Assert.NotNull(resolved);
        Assert.Equal("test_tool", resolved.Name);
    }

    [Fact]
    public void SandboxContext_ResolvesFromDI()
    {
        var services = new ServiceCollection();
        var sandbox = new FakeSandboxContext("/tmp/ws");
        services.AddSingleton<SandboxContext>(sandbox);
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<SandboxContext>();
        Assert.NotNull(resolved);
        Assert.Equal("/tmp/ws", resolved.WorkspacePath);
    }

    [Fact]
    public void TavilyWebSearchProvider_ResolvesFromDI()
    {
        var services = new ServiceCollection();
        var search = new FakeWebSearchProvider();
        services.AddSingleton<TavilyWebSearchProvider>(search);
        var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<TavilyWebSearchProvider>();
        Assert.NotNull(resolved);
        Assert.Equal("tavily", resolved.Name);
        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task TavilyWebSearchProvider_SearchAsync_ReturnsResults()
    {
        var provider = new FakeWebSearchProvider();
        var results = await provider.SearchAsync("test query", 5, CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.Equal("Test Result", results[0].Title);
    }

    [Fact]
    public async Task AllEightBuiltinTools_RegisteredInRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Register ToolRegistry + ToolRegistry
        services.AddSingleton<ToolRegistry, ToolRegistry>();

        // Register IToolImplementations
        var impls = new List<IToolImplementation>
        {
            new FakeToolImpl("read", "Read a file"),
            new FakeToolImpl("write", "Write a file"),
            new FakeToolImpl("edit", "Edit a file"),
            new FakeToolImpl("glob", "Find files by pattern"),
            new FakeToolImpl("grep", "Search file contents"),
            new FakeToolImpl("bash", "Execute a shell command"),
        };
        foreach (var impl in impls)
            services.AddSingleton<IToolImplementation>(impl);

        // Register WebFetchTool
        services.AddSingleton<HttpClient>();
        services.AddSingleton<WebFetchTool>();

        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ToolRegistry>();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ToolStartupRegistration>();
        var webFetchTool = provider.GetRequiredService<WebFetchTool>();
        var implementations = provider.GetRequiredService<IEnumerable<IToolImplementation>>();

        var registration = new ToolStartupRegistration(registry, implementations, webFetchTool, logger);
        await registration.StartAsync(CancellationToken.None);

        string[] expected = ["read", "write", "edit", "glob", "grep", "bash", "web_search", "web_fetch"];
        foreach (var name in expected)
        {
            var tool = registry.Resolve(name);
            Assert.True(tool is not null, $"Tool '{name}' should be registered");
            Assert.Equal(name, tool!.Name);
        }
    }

    [Fact]
    public async Task RegisteredTools_HaveCorrectSchemas()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ToolRegistry, ToolRegistry>();

        // Use real file tools for schema validation
        services.AddSingleton<IToolImplementation, ReadTool>();
        services.AddSingleton<IToolImplementation, WriteTool>();
        services.AddSingleton<IToolImplementation, EditTool>();
        services.AddSingleton<IToolImplementation, GlobTool>();
        services.AddSingleton<IToolImplementation, GrepTool>();
        services.AddSingleton<IToolImplementation, BashTool>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<WebFetchTool>();

        var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ToolRegistry>();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ToolStartupRegistration>();
        var webFetchTool = provider.GetRequiredService<WebFetchTool>();
        var implementations = provider.GetRequiredService<IEnumerable<IToolImplementation>>();

        var registration = new ToolStartupRegistration(registry, implementations, webFetchTool, logger);
        await registration.StartAsync(CancellationToken.None);

        // Verify each tool has a valid schema with required parameters
        foreach (var name in new[] { "read", "write", "edit", "glob", "grep", "bash" })
        {
            var tool = registry.Resolve(name);
            Assert.True(tool is not null, $"Tool '{name}' should be registered");
            Assert.True(tool!.ParametersSchema.ValueKind == System.Text.Json.JsonValueKind.Object,
                $"Tool '{name}' should have an object schema");
        }

        // web_search has "query" required
        var searchTool = registry.Resolve("web_search");
        Assert.True(searchTool is not null);
        var searchRequired = searchTool!.ParametersSchema.GetProperty("required");
        Assert.Contains("query", searchRequired.EnumerateArray().Select(x => x.GetString()));

        // web_fetch has "url" required
        var fetchTool = registry.Resolve("web_fetch");
        Assert.True(fetchTool is not null);
        var fetchRequired = fetchTool!.ParametersSchema.GetProperty("required");
        Assert.Contains("url", fetchRequired.EnumerateArray().Select(x => x.GetString()));
    }

    // ── Fake implementations for DI tests ──

    private sealed class FakeToolImpl : IToolImplementation
    {
        public string Name { get; }
        public string Description { get; }
        public JsonElement ParametersSchema => JsonDocument.Parse("{}").RootElement;

        public FakeToolImpl(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
            => Task.FromResult<object>("ok");
    }

    private sealed class FakeSandboxContext : SandboxContext
    {
        public FakeSandboxContext(string workspacePath) : base(workspacePath) { }
    }

    private sealed class FakeWebSearchProvider : TavilyWebSearchProvider
    {
        public FakeWebSearchProvider() : base(new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), null!) { }

        public new Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<WebSearchResult>>(new[]
            {
                new WebSearchResult("Test Result", "https://example.com", "A test snippet"),
            });
        }
    }
}
