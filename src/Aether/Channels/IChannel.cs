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

    /// <summary>
    /// Send a streaming text chunk to the channel. The channel may accumulate chunks
    /// and update the message in-place (e.g., edit_message on Telegram) or write
    /// individual chunks as they arrive (e.g., REPL). Callers should call
    /// <see cref="SendStreamingCompleteAsync"/> after the final chunk.
    /// chunkIndex is the zero-based position of this chunk in the stream.
    /// </summary>
    Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct);

    /// <summary>
    /// Signal that the stream is complete and the final message should be sent/saved.
    /// </summary>
    Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct);
}
