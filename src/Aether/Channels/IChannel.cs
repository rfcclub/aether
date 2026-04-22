namespace Aether.Channels;

public interface IChannel
{
    string Name { get; }
    bool IsConnected { get; }

    event EventHandler<InboundMessage>? OnMessage;

    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task SendMessageAsync(string chatId, string text, CancellationToken ct);
    Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct);
    bool OwnsChatId(string chatId);
}
