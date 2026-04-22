using Microsoft.Data.Sqlite;

namespace Aether.Data;

public sealed class AetherDb
{
    private readonly string _connectionString;
    private readonly string _schemaPath;

    public AetherDb(string databasePath, string schemaPath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            throw new ArgumentException("Schema path is required.", nameof(schemaPath));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        _schemaPath = schemaPath;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!File.Exists(_schemaPath))
        {
            throw new FileNotFoundException("Aether schema file was not found.", _schemaPath);
        }

        var schema = await File.ReadAllTextAsync(_schemaPath, ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$name", tableName);

        var result = await command.ExecuteScalarAsync(ct);
        return result is not null;
    }

    public async Task UpsertGroupRouteAsync(GroupRoute route, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO groups (jid, name, folder, is_main, requires_trigger, trigger, container_config)
            VALUES ($jid, $name, $folder, $isMain, $requiresTrigger, $trigger, NULL)
            ON CONFLICT(jid) DO UPDATE SET
                name = excluded.name,
                folder = excluded.folder,
                is_main = excluded.is_main,
                requires_trigger = excluded.requires_trigger,
                trigger = excluded.trigger;
            """;
        command.Parameters.AddWithValue("$jid", route.Jid);
        command.Parameters.AddWithValue("$name", route.Folder);
        command.Parameters.AddWithValue("$folder", route.Folder);
        command.Parameters.AddWithValue("$isMain", route.IsMain ? 1 : 0);
        command.Parameters.AddWithValue("$requiresTrigger", route.Trigger is null ? 0 : 1);
        command.Parameters.AddWithValue("$trigger", route.Trigger is null ? DBNull.Value : route.Trigger);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<GroupRoute?> GetGroupRouteAsync(string jid, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT jid, folder, is_main, trigger
            FROM groups
            WHERE jid = $jid
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$jid", jid);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var trigger = reader.IsDBNull(3) ? null : reader.GetString(3);
        return new GroupRoute(
            Jid: reader.GetString(0),
            Folder: reader.GetString(1),
            IsMain: reader.GetInt32(2) == 1,
            Trigger: trigger);
    }
}

public readonly record struct GroupRoute(string Jid, string Folder, bool IsMain, string? Trigger);
