using Aether.Channels;
using Aether.Ui;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

public sealed class TuiChannelService : IChannel, IHostedService
{
    public string Name => "tui";
    public bool IsConnected { get; private set; }
    
    public event EventHandler<InboundMessage>? OnMessage;
#pragma warning disable CS0067
    public event Func<string, UiCallback, Task<UiDocument?>>? OnUiCallback;
#pragma warning restore CS0067

    private readonly ILogger<TuiChannelService> _logger;

    public TuiChannelService(ILogger<TuiChannelService> logger)
    {
        _logger = logger;
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
        OnMessageReceived?.Invoke(text);
        return Task.CompletedTask;
    }

    public Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public bool OwnsChatId(string chatId) => chatId == "local";

    public Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct)
    {
        OnStreamingChunk?.Invoke(chunk);
        return Task.CompletedTask;
    }

    public Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct)
    {
        OnStreamingComplete?.Invoke();
        return Task.CompletedTask;
    }

    // HostedService
    public Task StartAsync(CancellationToken cancellationToken) => ConnectAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => DisconnectAsync(cancellationToken);

    // TUI Integration Events
    public Action<string>? OnMessageReceived { get; set; }
    public Action<string>? OnStreamingChunk { get; set; }
    public Action? OnStreamingComplete { get; set; }

    public void ReceiveUserInput(string text)
    {
        var msg = new InboundMessage(Guid.NewGuid().ToString(), "tui", "local", "user", text, DateTimeOffset.UtcNow);
        OnMessage?.Invoke(this, msg);
    }
}
