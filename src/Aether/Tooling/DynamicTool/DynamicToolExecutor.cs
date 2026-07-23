using System.Text.Json;
using Aether.Agent;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling.DynamicTool;

/// <summary>
/// Wraps dynamic tools (IDynamicTool) into the Aether tool call system.
/// Converts arguments from LlmToolCall to IDynamicTool.ExecuteAsync and back.
/// </summary>
public sealed class DynamicToolExecutor
{
    private readonly DynamicToolWatcherService _watcher;
    private readonly ILogger<DynamicToolExecutor> _logger;

    public DynamicToolExecutor(
        DynamicToolWatcherService watcher,
        ILogger<DynamicToolExecutor> logger)
    {
        _watcher = watcher;
        _logger = logger;
    }

    /// <summary>
    /// Try to execute a tool call as a dynamic tool.
    /// Returns true if the tool was found and executed, false if no matching dynamic tool exists.
    /// </summary>
    public async Task<(bool Handled, string Result)> TryExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, string>? arguments)
    {
        var tool = _watcher.Tools.FirstOrDefault(t =>
            string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (tool == null)
        {
            return (false, "");
        }

        _logger.LogInformation("Executing dynamic tool: {Name}", tool.Name);

        try
        {
            var args = ConvertArguments(arguments);

            // Apply timeout (default 30s) to prevent runaway tools
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var timeoutToken = timeoutCts.Token;

            var executeTask = tool.ExecuteAsync(args);
            var completed = await Task.WhenAny(executeTask, Task.Delay(-1, timeoutToken));

            if (completed == executeTask)
            {
                var result = await executeTask;
                _logger.LogInformation("Dynamic tool {Name} completed successfully", tool.Name);
                return (true, result);
            }
            else
            {
                _logger.LogWarning("Dynamic tool {Name} timed out after 30s", tool.Name);
                return (true, $"Error: {tool.Name} timed out after 30 seconds");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dynamic tool {Name} threw an exception", tool.Name);
            return (true, $"Error executing {tool.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert string arguments to the Dictionary&lt;string, object&gt; expected by IDynamicTool.
    /// Attempts to parse JSON values for richer types (numbers, booleans).
    /// </summary>
    private static Dictionary<string, object> ConvertArguments(IReadOnlyDictionary<string, string>? args)
    {
        var result = new Dictionary<string, object>();

        if (args == null)
            return result;

        foreach (var (key, value) in args)
        {
            if (TryParseJson(value, out var parsed))
            {
                result[key] = parsed ?? value;
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static bool TryParseJson(string value, out object? result)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;

            switch (root.ValueKind)
            {
                case JsonValueKind.Number:
                    if (root.TryGetInt64(out var longVal))
                    {
                        result = longVal;
                        return true;
                    }
                    if (root.TryGetDouble(out var doubleVal))
                    {
                        result = doubleVal;
                        return true;
                    }
                    result = value;
                    return true;

                case JsonValueKind.True:
                    result = true;
                    return true;

                case JsonValueKind.False:
                    result = false;
                    return true;

                case JsonValueKind.Array:
                case JsonValueKind.Object:
                    result = root.GetRawText();
                    return true;

                default:
                    result = value;
                    return false;
            }
        }
        catch
        {
            result = null;
            return false;
        }
    }
}
