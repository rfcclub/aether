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

    // Track streaming message IDs per chat so we can edit in-place.
    private readonly Dictionary<string, int> _streamingMessageIds = new();
    private readonly Dictionary<string, string> _lastStreamingText = new();

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
            parseMode: ParseMode.None,
            cancellationToken: ct);
    }

    public async Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("Telegram channel is not connected.");

        if (chunkIndex == 0)
        {
            var msg = await _client.SendMessage(
                chatId: chatId,
                text: chunk,
                parseMode: ParseMode.None,
                cancellationToken: ct);
            _streamingMessageIds[chatId] = msg.Id;
            _lastStreamingText[chatId] = chunk;
        }
        else if (_streamingMessageIds.TryGetValue(chatId, out var messageId))
        {
            try
            {
                await _client.EditMessageText(
                    chatId: chatId,
                    messageId: messageId,
                    text: chunk,
                    parseMode: ParseMode.None,
                    cancellationToken: ct);
                _lastStreamingText[chatId] = chunk;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to edit streaming message for chat {ChatId}", chatId);
            }
        }
    }

    public async Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct)
    {
        if (_client is null) return;

        if (_streamingMessageIds.TryGetValue(chatId, out var messageId))
        {
            // Skip edit if text hasn't changed since last chunk
            if (_lastStreamingText.TryGetValue(chatId, out var lastText) && lastText == fullText)
            {
                _streamingMessageIds.Remove(chatId);
                _lastStreamingText.Remove(chatId);
                return;
            }

            try
            {
                await _client.EditMessageText(
                    chatId: chatId,
                    messageId: messageId,
                    text: fullText,
                    parseMode: ParseMode.None,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to edit final streaming message for chat {ChatId}", chatId);
            }
        }
        else
        {
            await _client.SendMessage(
                chatId: chatId,
                text: fullText,
                parseMode: ParseMode.None,
                cancellationToken: ct);
        }

        _streamingMessageIds.Remove(chatId);
        _lastStreamingText.Remove(chatId);
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
            ChatId: message.Chat.Id.ToString(),
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
