using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

/// <summary>
/// Registers all built-in tools in ToolRegistry at startup.
/// Code-registered tools delegate to IToolImplementation for real execution.
/// </summary>
public sealed class ToolStartupRegistration : IHostedService
{
    private readonly ToolRegistry _registry;
    private readonly IEnumerable<IToolImplementation> _implementations;
    private readonly WebFetchTool _webFetchTool;
    private readonly TavilyWebSearchProvider? _searchProvider;
    private readonly ILogger _logger;

    public ToolStartupRegistration(
        ToolRegistry registry,
        IEnumerable<IToolImplementation> implementations,
        WebFetchTool webFetchTool,
        ILogger<ToolStartupRegistration> logger,
        TavilyWebSearchProvider? searchProvider = null)
    {
        _registry = registry;
        _implementations = implementations;
        _webFetchTool = webFetchTool;
        _searchProvider = searchProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var implMap = new Dictionary<string, IToolImplementation>(StringComparer.OrdinalIgnoreCase);
        foreach (var impl in _implementations)
            implMap[impl.Name] = impl;

        // Register file + shell tools from IToolImplementation
        foreach (var impl in _implementations)
        {
            _registry.Register(impl.Name, new ToolDefinition(
                impl.Name,
                impl.Description,
                impl.ParametersSchema,
                async (args, ct) =>
                {
                    // Sandbox context is resolved per-call from the current agent scope
                    var sandbox = ToolSandboxAccessor.Current
                                  ?? new SandboxContext(Directory.GetCurrentDirectory());
                    return await impl.ExecuteAsync(args, sandbox, ct);
                }));
            _logger.LogInformation("Registered built-in tool: {ToolName}", impl.Name);
        }

        // Register web_search tool
        var searchSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": { "type": "string", "description": "Search query" },
                    "limit": { "type": "integer", "description": "Max results (default 10, max 20)" }
                },
                "required": ["query"]
            }
            """).RootElement;

        _registry.Register("web_search", new ToolDefinition(
            "web_search",
            "Search the web using Tavily",
            searchSchema,
            async (args, ct) =>
            {
                if (_searchProvider is null)
                    throw new InvalidOperationException(
                        "web_search: no search provider configured. Set TAVILY_API_KEY.");

                var query = args.GetProperty("query").GetString()!;
                var limit = args.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number
                    ? l.GetInt32() : 10;

                var results = await _searchProvider.SearchAsync(query, limit, ct);
                if (results.Count == 0)
                    return "No results found.";

                var sb = new System.Text.StringBuilder();
                for (var i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    sb.AppendLine($"{i + 1}. **{r.Title}**");
                    sb.AppendLine($"   {r.Url}");
                    sb.AppendLine($"   {r.Snippet}");
                    sb.AppendLine();
                }
                return sb.ToString().TrimEnd();
            }));

        // Register web_fetch tool
        var fetchSchema = JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "url": { "type": "string", "description": "URL to fetch" }
                },
                "required": ["url"]
            }
            """).RootElement;

        _registry.Register("web_fetch", new ToolDefinition(
            "web_fetch",
            "Fetch and read a web page",
            fetchSchema,
            async (args, ct) =>
            {
                var url = args.GetProperty("url").GetString()!;
                return await _webFetchTool.ExecuteAsync(url, ct);
            }));

        _logger.LogInformation("Registered web tools: web_search, web_fetch");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Provides per-call sandbox context to tool implementations.
/// Set by ChannelMessageProcessor before tool dispatch.
/// </summary>
public static class ToolSandboxAccessor
{
    private static readonly AsyncLocal<SandboxContext?> _current = new();

    public static SandboxContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
