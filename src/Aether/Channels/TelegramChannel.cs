using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
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
            try { await _pollingTask.WaitAsync(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { _logger.LogWarning("Telegram polling task did not stop within 5 seconds"); }
        }

        _client = null;
        _logger.LogInformation("Telegram channel disconnected");
    }

    public async Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("Telegram channel is not connected.");
        await TryMarkdownAsync(mode => _client.SendMessage(chatId, text, parseMode: mode, cancellationToken: ct), ct);
    }

    public async Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("Telegram channel is not connected.");

        if (chunkIndex == 0)
        {
            var msgId = 0;
            await TryMarkdownAsync(async mode =>
            {
                var msg = await _client.SendMessage(chatId, chunk, parseMode: mode, cancellationToken: ct);
                msgId = msg.Id;
            }, ct);
            if (msgId != 0)
            {
                _streamingMessageIds[chatId] = msgId;
                _lastStreamingText[chatId] = chunk;
            }
        }
        else if (_streamingMessageIds.TryGetValue(chatId, out var messageId))
        {
            await EditWithFallbackAsync(chatId, messageId, chunk, ct);
        }
    }

    public async Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct)
    {
        if (_client is null) return;

        if (_streamingMessageIds.TryGetValue(chatId, out var messageId))
        {
            if (_lastStreamingText.TryGetValue(chatId, out var lastText) && lastText == fullText)
            {
                _streamingMessageIds.Remove(chatId);
                _lastStreamingText.Remove(chatId);
                return;
            }

            await EditWithFallbackAsync(chatId, messageId, fullText, ct);
        }
        else
        {
            await SendMessageAsync(chatId, fullText, ct);
        }

        _streamingMessageIds.Remove(chatId);
        _lastStreamingText.Remove(chatId);
    }

    public async Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct)
    {
        if (_client is null) return;
        if (isTyping)
            await _client.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);
    }

    public bool OwnsChatId(string chatId) => chatId.StartsWith("telegram:", StringComparison.Ordinal);

    // ── MarkdownV2 fallback helpers ──

    /// <summary>
    /// Try sending with MarkdownV2, fall back to plain text on parse error.
    /// </summary>
    private async Task TryMarkdownAsync(Func<ParseMode, Task> action, CancellationToken ct)
    {
        try
        {
            await action(ParseMode.MarkdownV2);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse"))
        {
            _logger.LogWarning(ex, "MarkdownV2 parse failed, falling back to plain text");
            await action(ParseMode.None);
        }
    }

    /// <summary>
    /// Edit a message with MarkdownV2, fall back to plain text on parse error.
    /// </summary>
    private async Task EditWithFallbackAsync(string chatId, int messageId, string text, CancellationToken ct)
    {
        try
        {
            await _client!.EditMessageText(chatId, messageId, text, parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
            _lastStreamingText[chatId] = text;
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse"))
        {
            try
            {
                await _client!.EditMessageText(chatId, messageId, text, parseMode: ParseMode.None, cancellationToken: ct);
                _lastStreamingText[chatId] = text;
            }
            catch (Exception innerEx)
            {
                _logger.LogWarning(innerEx, "Failed to edit message with plain text for chat {ChatId}", chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit message for chat {ChatId}", chatId);
        }
    }

    // ── Polling ──

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var updates = await _client!.GetUpdates(offset, timeout: 30,
                    allowedUpdates: new[] { UpdateType.Message }, cancellationToken: ct);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    if (update.Message is not { } message) continue;
                    if (string.IsNullOrWhiteSpace(message.Text)) continue;

                    var inbound = ConvertMessage(message);
                    if (inbound is not null)
                        OnMessage?.Invoke(this, inbound.Value);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
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
