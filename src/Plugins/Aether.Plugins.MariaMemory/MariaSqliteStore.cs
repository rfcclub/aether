using Microsoft.Data.Sqlite;
using Aether.Plugins.MariaMemory.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aether.Plugins.MariaMemory;

public sealed class MariaSqliteStore
{
    private readonly string _dbPath;
    private readonly ILogger _logger;

    public MariaSqliteStore(string workspacePath, ILogger logger)
    {
        _dbPath = Path.Combine(workspacePath, "store", "maria_memory.db");
        _logger = logger;
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS memory_nodes (
                id TEXT PRIMARY KEY,
                timestamp TEXT NOT NULL,
                role TEXT,
                content TEXT,
                tags TEXT,
                source TEXT,
                day_key TEXT,
                thread_id TEXT,
                weight REAL,
                score REAL,
                recall_count INTEGER,
                is_promoted INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_memory_nodes_thread ON memory_nodes(thread_id);
            CREATE INDEX IF NOT EXISTS idx_memory_nodes_day ON memory_nodes(day_key);
            
            CREATE TABLE IF NOT EXISTS edges (
                source_id TEXT,
                target_id TEXT,
                type TEXT,
                weight REAL,
                PRIMARY KEY (source_id, target_id, type)
            );

            -- FTS5 virtual table for full-text search on memory nodes
            CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
                content, 
                content='memory_nodes', 
                content_rowid='rowid'
            );

            -- Triggers to keep FTS5 in sync with memory_nodes table
            CREATE TRIGGER IF NOT EXISTS memory_nodes_ai AFTER INSERT ON memory_nodes BEGIN
                INSERT INTO memory_fts(rowid, content) VALUES (new.rowid, new.content);
            END;

            CREATE TRIGGER IF NOT EXISTS memory_nodes_ad AFTER DELETE ON memory_nodes BEGIN
                INSERT INTO memory_fts(memory_fts, rowid, content) VALUES('delete', old.rowid, old.content);
            END;

            CREATE TRIGGER IF NOT EXISTS memory_nodes_au AFTER UPDATE ON memory_nodes BEGIN
                INSERT INTO memory_fts(memory_fts, rowid, content) VALUES('delete', old.rowid, old.content);
                INSERT INTO memory_fts(rowid, content) VALUES (new.rowid, new.content);
            END;
        ";
        command.ExecuteNonQuery();
    }

    public async Task UpsertAsync(MemoryNode node, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO memory_nodes (id, timestamp, role, content, tags, source, day_key, thread_id, weight, score, recall_count, is_promoted)
            VALUES (@id, @ts, @role, @content, @tags, @source, @day, @thread, @weight, @score, @recall, @promoted)
            ON CONFLICT(id) DO UPDATE SET
                content = EXCLUDED.content,
                tags = EXCLUDED.tags,
                score = EXCLUDED.score,
                recall_count = EXCLUDED.recall_count,
                is_promoted = EXCLUDED.is_promoted;
        ";
        command.Parameters.AddWithValue("@id", node.Id);
        command.Parameters.AddWithValue("@ts", node.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("@role", node.Role);
        command.Parameters.AddWithValue("@content", node.Content);
        command.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(node.Tags));
        command.Parameters.AddWithValue("@source", node.Source);
        command.Parameters.AddWithValue("@day", node.DayKey);
        command.Parameters.AddWithValue("@thread", node.ThreadId);
        command.Parameters.AddWithValue("@weight", node.Weight);
        command.Parameters.AddWithValue("@score", node.Score);
        command.Parameters.AddWithValue("@recall", node.RecallCount);
        command.Parameters.AddWithValue("@promoted", node.IsPromoted ? 1 : 0);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<MemoryNode>> GetAllNodesAsync(int limit = 1000, CancellationToken ct = default)
    {
        var results = new List<MemoryNode>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, timestamp, role, content, tags, source, day_key, thread_id, weight, score, recall_count, is_promoted 
            FROM memory_nodes 
            ORDER BY timestamp DESC
            LIMIT @limit;
        ";
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapReaderToNode(reader));
        }

        return results;
    }

    public async Task<List<MemoryNode>> GetPromotionCandidatesAsync(float minScore = 5.0f, int limit = 10, CancellationToken ct = default)
    {
        var results = new List<MemoryNode>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, timestamp, role, content, tags, source, day_key, thread_id, weight, score, recall_count, is_promoted 
            FROM memory_nodes 
            WHERE is_promoted = 0 AND score >= @minScore
            ORDER BY score DESC
            LIMIT @limit;
        ";
        command.Parameters.AddWithValue("@minScore", minScore);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapReaderToNode(reader));
        }

        return results;
    }

    public async Task UpdatePromotionStatusAsync(string id, bool isPromoted, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE memory_nodes SET is_promoted = @promoted WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@promoted", isPromoted ? 1 : 0);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task CreateEdgeAsync(string sourceId, string targetId, string type, float weight = 1.0f, CancellationToken ct = default)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO edges (source_id, target_id, type, weight)
            VALUES (@source, @target, @type, @weight)
            ON CONFLICT(source_id, target_id, type) DO UPDATE SET weight = EXCLUDED.weight;
        ";
        command.Parameters.AddWithValue("@source", sourceId);
        command.Parameters.AddWithValue("@target", targetId);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@weight", weight);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<(string targetId, string type, float weight)>> GetEdgesAsync(string sourceId, CancellationToken ct = default)
    {
        var results = new List<(string, string, float)>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT target_id, type, weight FROM edges WHERE source_id = @source";
        command.Parameters.AddWithValue("@source", sourceId);

        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetFloat(2)));
        }

        return results;
    }

    public async Task<List<MemoryNode>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        var results = new List<MemoryNode>();
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT n.id, n.timestamp, n.role, n.content, n.tags, n.source, n.day_key, n.thread_id, n.weight, n.score, n.recall_count, n.is_promoted 
            FROM memory_nodes n
            JOIN memory_fts f ON n.rowid = f.rowid
            WHERE memory_fts MATCH @query
            ORDER BY n.score DESC, n.timestamp DESC
            LIMIT @limit;
        ";
        command.Parameters.AddWithValue("@query", query);
        command.Parameters.AddWithValue("@limit", limit);

        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapReaderToNode(reader));
        }

        return results;
    }

    private static MemoryNode MapReaderToNode(SqliteDataReader reader)
    {
        return new MemoryNode
        {
            Id = reader.GetString(0),
            Timestamp = DateTime.Parse(reader.GetString(1)),
            Role = reader.GetString(2),
            Content = reader.GetString(3),
            Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? new(),
            Source = reader.GetString(5),
            DayKey = reader.GetString(6),
            ThreadId = reader.GetString(7),
            Weight = reader.GetFloat(8),
            Score = reader.GetFloat(9),
            RecallCount = reader.GetInt32(10),
            IsPromoted = reader.GetInt32(11) == 1
        };
    }
}
