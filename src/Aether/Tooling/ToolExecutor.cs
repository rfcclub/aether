using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public class ToolExecutor
{
    private readonly ToolRegistry _registry;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(ToolRegistry registry, ILogger<ToolExecutor> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(string name, string jsonArgs, CancellationToken ct = default)
    {
        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(jsonArgs);
            return await ExecuteAsync(name, args, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse tool arguments for {ToolName}", name);
            return new ToolResult(false, null, $"Invalid JSON arguments: {ex.Message}");
        }
    }

    public async Task<ToolResult> ExecuteAsync(string name, JsonElement args, CancellationToken ct = default)
    {
        var tool = _registry.Resolve(name);
        if (tool is null)
        {
            _logger.LogWarning("Tool not found: {ToolName}", name);
            return new ToolResult(false, null, $"Tool '{name}' not found");
        }

        if (!tool.Enabled)
        {
            var reason = string.IsNullOrWhiteSpace(tool.DisabledReason)
                ? $"Tool '{name}' not permitted"
                : tool.DisabledReason;
            _logger.LogWarning("Tool disabled: {ToolName} ({Reason})", name, reason);
            return new ToolResult(false, null, reason);
        }

        try
        {
            _logger.LogDebug("Executing tool: {ToolName}", name);
            var result = await tool.Execute(args, ct);
            return new ToolResult(true, result, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Tool execution cancelled: {ToolName}", name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", name);
            return new ToolResult(false, null, ex.Message);
        }
    }
}
