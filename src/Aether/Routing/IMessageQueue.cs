namespace Aether.Routing;

public interface IMessageQueue
{
    ValueTask EnqueueAsync(RoutedMessage message, CancellationToken ct);
    ValueTask<RoutedMessage> ReadAsync(CancellationToken ct);
}
