using Aether.Data;

namespace Aether.Tests;

public class AetherDbTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AetherDb _db;

    public AetherDbTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether-db-test-{Guid.NewGuid():N}.db");
        var schemaPath = FindSchemaPath();
        _db = new AetherDb(_dbPath, schemaPath);
    }

    [Fact]
    public async Task Initialize_CreatesTables()
    {
        await _db.InitializeAsync(CancellationToken.None);

        Assert.True(await _db.TableExistsAsync("messages"));
        Assert.True(await _db.TableExistsAsync("sessions"));
        Assert.True(await _db.TableExistsAsync("tasks"));
        Assert.True(await _db.TableExistsAsync("groups"));
        Assert.True(await _db.TableExistsAsync("task_runs"));
    }

    [Fact]
    public async Task Initialize_Idempotent()
    {
        await _db.InitializeAsync(CancellationToken.None);
        await _db.InitializeAsync(CancellationToken.None);
        Assert.True(await _db.TableExistsAsync("messages"));
    }

    [Fact]
    public async Task UpsertGroupRoute_AndGetGroupRoute_Roundtrip()
    {
        await _db.InitializeAsync(CancellationToken.None);
        var expected = new GroupRoute("telegram:12345", "main", true, "@Aether");
        await _db.UpsertGroupRouteAsync(expected);

        var actual = await _db.GetGroupRouteAsync("telegram:12345");

        Assert.NotNull(actual);
        Assert.Equal(expected.Folder, actual!.Value.Folder);
        Assert.Equal(expected.IsMain, actual.Value.IsMain);
        Assert.Equal(expected.Trigger, actual.Value.Trigger);
    }

    [Fact]
    public async Task GetGroupRoute_Missing_ReturnsNull()
    {
        await _db.InitializeAsync(CancellationToken.None);
        var route = await _db.GetGroupRouteAsync("nonexistent:123");
        Assert.Null(route);
    }

    [Fact]
    public async Task UpsertGroupRoute_UpdateExisting_Overwrites()
    {
        await _db.InitializeAsync(CancellationToken.None);
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:12345", "main", true, null));
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:12345", "main", false, "@Bot"));

        var actual = await _db.GetGroupRouteAsync("telegram:12345");
        Assert.False(actual!.Value.IsMain);
        Assert.Equal("@Bot", actual.Value.Trigger);
    }

    [Fact]
    public async Task RecordProviderUsage_Persists()
    {
        await _db.InitializeAsync(CancellationToken.None);
        var usage = new ProviderUsage(
            Id: "usage-1",
            Provider: "openrouter",
            Model: "claude-3-5-sonnet",
            InputTokens: 100,
            OutputTokens: 50,
            CostUsd: 0.001,
            LatencyMs: 234,
            Timestamp: DateTimeOffset.UtcNow);

        await _db.RecordProviderUsageAsync(usage);
        // No exception = success (can't read back without adding query method)
    }

    [Fact]
    public void Constructor_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AetherDb("", "schema.sql"));
    }

    [Fact]
    public async Task Initialize_MissingSchemaFile_Throws()
    {
        var db = new AetherDb(_dbPath, "/nonexistent/schema.sql");
        await Assert.ThrowsAsync<FileNotFoundException>(() => db.InitializeAsync(CancellationToken.None));
    }

    private static string FindSchemaPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aether", "Data", "Schema.sql"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("Cannot find Schema.sql");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
