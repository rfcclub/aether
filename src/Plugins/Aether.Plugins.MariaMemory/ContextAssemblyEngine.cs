using System.Text;
using Aether.Plugins.MariaMemory.Models;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class ContextAssemblyEngine
{
    private readonly MariaMemoryStore _store;
    private readonly ILogger _logger;

    public ContextAssemblyEngine(MariaMemoryStore store, ILogger logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<string> AssembleContextAsync(string topic, int tokenLimit = 7000, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        _logger.LogInformation("Assembling smart context for topic: {Topic}", topic);

        // 1. Identity Baseline (Bootstrap) - usually already in Aether's ContextAssembler, 
        // but we can add specific long-term insights here.
        sb.AppendLine("## Maria Memory Context");

        // 2. Query relevant nodes
        var nodes = await _store.SearchAsync(topic, limit: 20, ct);
        
        // 3. Score and Sort (Scoring is already done in SQLite Search if we used it, 
        // but we can refine here based on recency)
        var orderedNodes = nodes.OrderByDescending(n => n.Score).ToList();

        int currentTokens = 0;
        foreach (var node in orderedNodes)
        {
            var formatted = $"\n### {node.Timestamp:yyyy-MM-dd} [{node.Role}]\n{node.Content}\n";
            int tokens = formatted.Length / 4; // Simple estimate

            if (currentTokens + tokens > tokenLimit) break;

            sb.Append(formatted);
            currentTokens += tokens;
            
            // Mark node as recalled
            node.RecallCount++;
            await _store.AppendAsync(node, ct); // Update in store (via Upsert in SQLite)
        }

        return sb.ToString();
    }
}
