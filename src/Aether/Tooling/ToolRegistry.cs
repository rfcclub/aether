using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public enum ToolRisk
{
    Read,
    Write,
    Exec,
    Network,
    ExternalSend,
    OwnerOnly
}

public record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    Func<JsonElement, CancellationToken, Task<object>> Execute,
    ToolRisk Risk = ToolRisk.Read,
    bool Enabled = true,
    string? DisabledReason = null
);

public record ToolResult(bool Success, object? Data, string? Error);

public sealed record ToolDescriptor(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    ToolRisk Risk,
    bool Enabled,
    string? DisabledReason);

public class ToolRegistry 
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

    public IReadOnlyList<ToolDefinition> ListDefinitions(bool includeDisabled = false) =>
        _tools.Values
            .Where(t => includeDisabled || t.Enabled)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<ToolDescriptor> Audit() =>
        _tools.Values
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new ToolDescriptor(
                t.Name,
                t.Description,
                t.ParametersSchema,
                t.Risk,
                t.Enabled,
                t.DisabledReason))
            .ToList();

    public bool HasTool(string name) => _tools.ContainsKey(name);
}
