using Aether.Ui;

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

    // ── Interactive UI ──

    /// <summary>
    /// Fired when a user interacts with an interactive message (e.g., taps an inline keyboard button).
    /// The handler receives the parsed callback and returns an optional updated UiDocument,
    /// or null to acknowledge without editing the message.
    /// </summary>
    event Func<UiCallback, Task<UiDocument?>>? OnUiCallback;

    /// <summary>
    /// Send an interactive message to a chat. Returns the channel-specific message identifier,
    /// or null if the channel does not support interactive messages.
    /// </summary>
    Task<string?> SendInteractiveAsync(string chatId, UiDocument doc)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Edit an existing interactive message in-place. If the message no longer exists
    /// (e.g., user deleted it), the channel should fall back to sending a new message.
    /// </summary>
    Task EditInteractiveAsync(string chatId, string messageId, UiDocument doc)
    {
        return Task.CompletedTask;
    }
}
