namespace Aether.Channels;

public sealed class NoOpChannel : IChannel
{
    public string Name => "noop";
    public bool IsConnected => false;

    public event EventHandler<InboundMessage>? OnMessage;

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SendMessageAsync(string chatId, string text, CancellationToken ct) => Task.CompletedTask;
    public Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct) => Task.CompletedTask;
    public bool OwnsChatId(string chatId) => false;
    public Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct) => Task.CompletedTask;
    public Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct) => Task.CompletedTask;
}
