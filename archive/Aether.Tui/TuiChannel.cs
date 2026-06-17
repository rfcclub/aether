using Aether.Channels;
using Aether.Ui;

namespace Aether.Tui;

public sealed class TuiChannel : IChannel
{
    public string Name => "tui";
    public bool IsConnected { get; private set; }
    
    public event EventHandler<InboundMessage>? OnMessage;
    public event Func<string, UiCallback, Task<UiDocument?>>? OnUiCallback;

    // Mutable callbacks — wired after UI is built
    private Action<string> _onAppended;
    private Action<string> _onStreamingChunk;
    private Action _onStreamingComplete;

    public TuiChannel(Action<string> onAppended, Action<string> onStreamingChunk, Action onStreamingComplete)
    {
        _onAppended = onAppended;
        _onStreamingChunk = onStreamingChunk;
        _onStreamingComplete = onStreamingComplete;
    }

    /// <summary>Re-wire callbacks after UI is ready (deferred init).</summary>
    public void SetCallbacks(Action<string> onAppended, Action<string> onStreamingChunk, Action onStreamingComplete)
    {
        _onAppended = onAppended;
        _onStreamingChunk = onStreamingChunk;
        _onStreamingComplete = onStreamingComplete;
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        _onAppended(text);
        return Task.CompletedTask;
    }

    public Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public bool OwnsChatId(string chatId) => true;

    public Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct)
    {
        _onStreamingChunk(chunk);
        return Task.CompletedTask;
    }

    public Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct)
    {
        _onStreamingComplete();
        return Task.CompletedTask;
    }

    public void ReceiveUserInput(string text)
    {
        var msg = new InboundMessage(Guid.NewGuid().ToString(), "tui", "local", "user", text, DateTimeOffset.UtcNow);
        OnMessage?.Invoke(this, msg);
    }
}
