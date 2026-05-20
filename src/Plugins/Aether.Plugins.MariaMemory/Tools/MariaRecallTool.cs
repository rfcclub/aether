using System.Text.Json;
using Aether.Plugins;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins.MariaMemory.Tools;

public sealed class MariaRecallTool : IToolImplementation
{
    public string Name => "maria_recall";
    public string Description => "Search Maria's long-term memory index.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "query": {
                "type": "string",
                "description": "The search query (keywords or phrases)"
            },
            "limit": {
                "type": "integer",
                "description": "Maximum number of results to return",
                "default": 10
            }
        },
        "required": ["query"]
    }
    """).RootElement;

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString() ?? "";
        var limit = 10;
        if (args.TryGetProperty("limit", out var limitProp))
        {
            if (limitProp.ValueKind == JsonValueKind.Number)
            {
                limit = limitProp.GetInt32();
            }
            else if (limitProp.ValueKind == JsonValueKind.String && int.TryParse(limitProp.GetString(), out var l))
            {
                limit = l;
            }
        }

        var workspacePath = sandbox.WorkspacePath;
        
        var store = new MariaMemoryStore(workspacePath, NullLogger.Instance);
        var results = await store.SearchAsync(query, limit, ct);

        return new { success = true, nodes = results };
    }
}
