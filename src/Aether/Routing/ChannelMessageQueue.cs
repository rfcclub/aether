using System.Threading.Channels;

namespace Aether.Routing;

public sealed class ChannelMessageQueue 
{
    private readonly Channel<RoutedMessage> _channel;

    public ChannelMessageQueue()
    {
        _channel = Channel.CreateUnbounded<RoutedMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(RoutedMessage message, CancellationToken ct)
    {
        return _channel.Writer.WriteAsync(message, ct);
    }

    public ValueTask<RoutedMessage> ReadAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAsync(ct);
    }
}
