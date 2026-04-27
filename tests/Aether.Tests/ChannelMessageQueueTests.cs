using Aether.Routing;
using Aether.Channels;

namespace Aether.Tests;

public class ChannelMessageQueueTests
{
    [Fact]
    public async Task Enqueue_And_Read_Roundtrips()
    {
        var queue = new ChannelMessageQueue();
        var inbound = new InboundMessage("msg-1", "telegram", "chat-1", "user1", "hello", DateTimeOffset.UtcNow);
        var routed = new RoutedMessage(inbound, "main", "hello");

        await queue.EnqueueAsync(routed, CancellationToken.None);
        var result = await queue.ReadAsync(CancellationToken.None);

        Assert.Equal("main", result.GroupFolder);
        Assert.Equal("hello", result.Prompt);
        Assert.Equal("msg-1", result.Inbound.Id);
    }

    [Fact]
    public async Task Enqueue_MultipleMessages_PreservesOrder()
    {
        var queue = new ChannelMessageQueue();
        for (var i = 0; i < 5; i++)
        {
            var inbound = new InboundMessage($"msg-{i}", "telegram", "chat-1", "user1", $"msg{i}", DateTimeOffset.UtcNow);
            await queue.EnqueueAsync(new RoutedMessage(inbound, "main", $"msg{i}"), CancellationToken.None);
        }

        for (var i = 0; i < 5; i++)
        {
            var result = await queue.ReadAsync(CancellationToken.None);
            Assert.Equal($"msg{i}", result.Prompt);
        }
    }
}
