using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public interface IToolRegistry
{
    void Register(string name, ToolDefinition tool);
    void Unregister(string name);
    ToolDefinition? Resolve(string name);
    IEnumerable<string> List();
    bool HasTool(string name);
}

public record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    Func<JsonElement, CancellationToken, Task<object>> Execute
);

public record ToolResult(bool Success, object? Data, string? Error);

public class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(ILogger<ToolRegistry> logger)
    {
        _logger = logger;
    }

    public void Register(string name, ToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name cannot be empty", nameof(name));

        _tools[name] = tool;
        _logger.LogInformation("Registered tool: {ToolName}", name);
    }

    public void Unregister(string name)
    {
        if (_tools.TryRemove(name, out _))
        {
            _logger.LogInformation("Unregistered tool: {ToolName}", name);
        }
    }

    public ToolDefinition? Resolve(string name) =>
        _tools.TryGetValue(name, out var tool) ? tool : null;

    public IEnumerable<string> List() => _tools.Keys.OrderBy(k => k);

    public bool HasTool(string name) => _tools.ContainsKey(name);
}
