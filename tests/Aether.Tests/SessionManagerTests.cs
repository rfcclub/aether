using Aether.Sessions;
using Aether.Data;

namespace Aether.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AetherDb _db;
    private readonly SessionManager _sessions;

    public SessionManagerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether-session-test-{Guid.NewGuid():N}.db");
        var schemaPath = FindSchemaPath();
        _db = new AetherDb(_dbPath, schemaPath);
        _db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _sessions = new SessionManager(_db);
    }

    [Fact]
    public async Task GetOrCreateSession_FirstCall_CreatesSession()
    {
        var session = await _sessions.GetOrCreateSessionAsync("main");
        Assert.NotNull(session);
        Assert.Equal("main", session.GroupFolder);
        Assert.NotEmpty(session.Id);
    }

    [Fact]
    public async Task GetOrCreateSession_SameGroup_ReusesSession()
    {
        var first = await _sessions.GetOrCreateSessionAsync("main");
        var second = await _sessions.GetOrCreateSessionAsync("main");

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task GetOrCreateSession_DifferentGroups_CreatesSeparateSessions()
    {
        var first = await _sessions.GetOrCreateSessionAsync("main");
        var second = await _sessions.GetOrCreateSessionAsync("other");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task AppendMessage_AndGetHistory_Roundtrips()
    {
        var session = await _sessions.GetOrCreateSessionAsync("main");
        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", "hello", DateTimeOffset.UtcNow));
        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("assistant", "hi there", DateTimeOffset.UtcNow));

        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 10);

        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("hello", history[0].Content);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("hi there", history[1].Content);
    }

    [Fact]
    public async Task AppendMessage_InvalidSession_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sessions.AppendMessageAsync("nonexistent", new SessionMessage("user", "test", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task GetHistory_RespectsMaxMessages()
    {
        var session = await _sessions.GetOrCreateSessionAsync("main");
        for (var i = 0; i < 10; i++)
        {
            await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", $"msg{i}", DateTimeOffset.UtcNow));
        }

        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 5);
        Assert.Equal(5, history.Count);
    }

    [Fact]
    public async Task GetHistory_EmptySession_ReturnsEmpty()
    {
        var session = await _sessions.GetOrCreateSessionAsync("main");
        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 10);
        Assert.Empty(history);
    }

    [Fact]
    public async Task GetRecentSessions_ReturnsSessionsOrderedByActivity()
    {
        var a = await _sessions.GetOrCreateSessionAsync("alpha");
        await Task.Delay(10);
        var b = await _sessions.GetOrCreateSessionAsync("beta");
        await Task.Delay(10);
        var c = await _sessions.GetOrCreateSessionAsync("gamma");

        var recent = await _sessions.GetRecentSessionsAsync(limit: 3);

        Assert.Equal(3, recent.Count);
        Assert.Equal("gamma", recent[0].GroupFolder);
        Assert.Equal("beta", recent[1].GroupFolder);
        Assert.Equal("alpha", recent[2].GroupFolder);
    }

    [Fact]
    public async Task GetRecentSessions_RespectsLimit()
    {
        await _sessions.GetOrCreateSessionAsync("a");
        await _sessions.GetOrCreateSessionAsync("b");
        await _sessions.GetOrCreateSessionAsync("c");

        var recent = await _sessions.GetRecentSessionsAsync(limit: 2);

        Assert.Equal(2, recent.Count);
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
