using Aether.Routing;
using Aether.Channels;
using Aether.Data;

namespace Aether.Tests;

public class MessageRouterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AetherDb _db;

    public MessageRouterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether-router-test-{Guid.NewGuid():N}.db");
        _db = new AetherDb(_dbPath, FindSchemaPath());
        _db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task RouteAsync_RegisteredGroup_Routes()
    {
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:chat-1", "main", true, null));
        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(_db, queue);

        var inbound = new InboundMessage("msg-1", "telegram", "chat-1", "user1", "hello world", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(inbound);

        Assert.NotNull(result);
        Assert.Equal("main", result!.Value.WorkspacePath);
        Assert.Equal("hello world", result.Value.Prompt);
    }

    [Fact]
    public async Task RouteAsync_WithTrigger_StripsTrigger()
    {
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:chat-1", "main", true, "@Aether"));
        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(_db, queue);

        var inbound = new InboundMessage("msg-1", "telegram", "chat-1", "user1", "@Aether status check", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(inbound);

        Assert.NotNull(result);
        Assert.Equal("status check", result!.Value.Prompt);
    }

    [Fact]
    public async Task RouteAsync_WithTrigger_WrongTrigger_ReturnsNull()
    {
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:chat-1", "main", true, "@Bot"));
        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(_db, queue);

        var inbound = new InboundMessage("msg-1", "telegram", "chat-1", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task RouteAsync_UnregisteredGroup_ReturnsNull()
    {
        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(_db, queue);

        var inbound = new InboundMessage("msg-1", "telegram", "unknown-chat", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task RouteAsync_FromBot_Skips()
    {
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:chat-1", "main", true, null));
        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(_db, queue);

        var inbound = new InboundMessage("msg-1", "telegram", "chat-1", "bot", "echo", DateTimeOffset.UtcNow, IsFromBot: true);
        var result = await router.RouteAsync(inbound);

        Assert.Null(result);
    }

    [Fact]
    public async Task RouteAsync_EnqueuesMessage()
    {
        await _db.UpsertGroupRouteAsync(new GroupRoute("telegram:chat-1", "main", true, null));
        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(_db, queue);

        var inbound = new InboundMessage("msg-1", "telegram", "chat-1", "user1", "hello", DateTimeOffset.UtcNow);
        await router.RouteAsync(inbound);

        var dequeued = await queue.ReadAsync(CancellationToken.None);
        Assert.Equal("hello", dequeued.Prompt);
        Assert.Equal("main", dequeued.WorkspacePath);
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
