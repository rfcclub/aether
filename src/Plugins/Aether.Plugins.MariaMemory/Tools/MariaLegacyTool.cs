using System.Text.Json;
using Aether.Plugins;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins.MariaMemory.Tools;

public sealed class MariaLegacyTool : IToolImplementation
{
    public string Name => "mariamem_bridge";
    public string Description => "Call legacy Maria memory tools via Python bridge.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "tool_name": { "type": "string" },
            "arguments": { "type": "object" }
        },
        "required": ["tool_name"]
    }
    """).RootElement;

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var toolName = args.GetProperty("tool_name").GetString() ?? "";
        var toolArgs = args.TryGetProperty("arguments", out var a) ? (object)a : new { };

        // The bridge scripts are located in the workspace extension folder
        var bridgeDir = Path.Combine(sandbox.WorkspacePath, "extension", "maria-memory");
        
        var bridge = new LegacyPythonBridge(bridgeDir, NullLogger.Instance);
        var result = await bridge.CallLegacyToolAsync(toolName, toolArgs, ct);

        return JsonDocument.Parse(result).RootElement;
    }
}
