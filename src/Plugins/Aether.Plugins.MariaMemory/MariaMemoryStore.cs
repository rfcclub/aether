using System.Text.Json;
using Aether.Plugins.MariaMemory.Models;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class MariaMemoryStore
{
    private readonly string _workspacePath;
    private readonly ILogger _logger;
    private readonly string _indexPath;
    private readonly MariaSqliteStore _sqlite;

    public MariaMemoryStore(string workspacePath, ILogger logger)
    {
        _workspacePath = workspacePath;
        _logger = logger;
        _indexPath = Path.Combine(workspacePath, "store", "maria_index.jsonl");
        _sqlite = new MariaSqliteStore(workspacePath, logger);
        
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
    }

    public async Task AppendAsync(MemoryNode node, CancellationToken ct = default)
    {
        try
        {
            // JSONL Write (Legacy/Transparency)
            var line = JsonSerializer.Serialize(node);
            await File.AppendAllLinesAsync(_indexPath, new[] { line }, ct);

            // SQLite Write (Advanced queries/Graph)
            await _sqlite.UpsertAsync(node, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append memory node to store (dual-write)");
        }
    }

    public async Task<List<MemoryNode>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        // Prefer SQLite for search as it has FTS5 and better ordering
        return await _sqlite.SearchAsync(query, limit, ct);
    }

    public async Task<List<MemoryNode>> GetAllNodesAsync(int limit = 100, CancellationToken ct = default)
    {
        return await _sqlite.GetAllNodesAsync(limit, ct);
    }
}
