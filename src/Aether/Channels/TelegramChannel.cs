using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Aether.Channels;

public sealed class TelegramChannel : IChannel, IDisposable
{
    private readonly string _botToken;
    private readonly ILogger<TelegramChannel> _logger;
    private TelegramBotClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private bool _disposed;

    public string Name => "telegram";
    public bool IsConnected => _client is not null;

    public event EventHandler<InboundMessage>? OnMessage;

    public TelegramChannel(string botToken, ILogger<TelegramChannel> logger)
    {
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _client = new TelegramBotClient(_botToken);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var me = await _client.GetMe(ct);
        _logger.LogInformation("Telegram channel connected as @{Username} (id={Id})", me.Username, me.Id);

        _pollingTask = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("Telegram polling task did not stop within 5 seconds");
            }
        }

        _client = null;
        _logger.LogInformation("Telegram channel disconnected");
    }

    public async Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("Telegram channel is not connected.");

        await _client.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: ParseMode.None, // Plain text by default; caller can use markdown if needed
            cancellationToken: ct);
    }

    public async Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct)
    {
        if (_client is null) return;

        if (isTyping)
        {
            await _client.SendChatAction(
                chatId: chatId,
                action: ChatAction.Typing,
                cancellationToken: ct);
        }
    }

    public bool OwnsChatId(string chatId)
    {
        return chatId.StartsWith("telegram:", StringComparison.Ordinal);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var offset = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var updates = await _client!.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    allowedUpdates: new[] { UpdateType.Message },
                    cancellationToken: ct);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    if (update.Message is not { } message) continue;
                    if (string.IsNullOrWhiteSpace(message.Text)) continue;

                    var inbound = ConvertMessage(message);
                    if (inbound is not null)
                    {
                        OnMessage?.Invoke(this, inbound.Value);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram polling error");
                await Task.Delay(5000, ct);
            }
        }
    }

    private static InboundMessage? ConvertMessage(Message message)
    {
        if (message.From is null || string.IsNullOrWhiteSpace(message.Text))
            return null;

        return new InboundMessage(
            Id: message.MessageId.ToString(),
            ChannelName: "telegram",
            ChatId: $"telegram:{message.Chat.Id}",
            SenderId: message.From.Username ?? message.From.Id.ToString(),
            Text: message.Text,
            Timestamp: message.Date,
            IsFromBot: message.From.IsBot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
