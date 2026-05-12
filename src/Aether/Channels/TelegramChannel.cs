using Aether.Ui;
using Aether.Ui.Renderers;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Aether.Channels;

public sealed class TelegramChannel : IChannel, IDisposable
{
    private readonly string _botToken;
    private readonly ILogger<TelegramChannel> _logger;
    private readonly TelegramUiRenderer _uiRenderer = new();
    private TelegramBotClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private bool _disposed;

    private readonly Dictionary<string, int> _streamingMessageIds = new();
    private readonly Dictionary<string, string> _lastStreamingText = new();

    public string Name => "telegram";
    public bool IsConnected => _client is not null;

    public event EventHandler<InboundMessage>? OnMessage;
    public event Func<string, UiCallback, Task<UiDocument?>>? OnUiCallback;

    public TelegramChannel(string botToken, ILogger<TelegramChannel> logger)
    {
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
        _logger = logger;
    }

    private static readonly BotCommand[] NativeCommands =
    {
        new() { Command = "new", Description = "Start a new session" },
        new() { Command = "reset", Description = "Clear context" },
        new() { Command = "model", Description = "Show or switch model" },
        new() { Command = "context", Description = "Show context info" },
        new() { Command = "compact", Description = "Compact context" },
    };

    public async Task ConnectAsync(CancellationToken ct)
    {
        _client = new TelegramBotClient(_botToken);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var me = await _client.GetMe(ct);
        _logger.LogInformation("Telegram channel connected as @{Username} (id={Id})", me.Username, me.Id);

        await _client.SetMyCommands(NativeCommands, cancellationToken: ct);
        _logger.LogInformation("Telegram native commands registered: {Commands}",
            string.Join(", ", NativeCommands.Select(c => "/" + c.Command)));

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
        await TryMarkdownAsync(
            mode => _client.SendMessage(chatId, text, parseMode: mode, cancellationToken: ct),
            () => _client.SendMessage(chatId, text, cancellationToken: ct),
            ct);
    }

    public async Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("Telegram channel is not connected.");

        if (chunkIndex == 0)
        {
            var msgId = 0;
            await TryMarkdownAsync(
                async mode =>
                {
                    var msg = await _client.SendMessage(chatId, chunk, parseMode: mode, cancellationToken: ct);
                    msgId = msg.Id;
                },
                async () =>
                {
                    var msg = await _client.SendMessage(chatId, chunk, cancellationToken: ct);
                    msgId = msg.Id;
                },
                ct);
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

    // ── Interactive UI ──

    public async Task<string?> SendInteractiveAsync(string chatId, UiDocument doc)
    {
        if (_client is null) return null;

        var (text, markup) = ((string, InlineKeyboardMarkup))_uiRenderer.Render(doc);
        Message msg;
        try
        {
            msg = await _client.SendMessage(chatId, text, replyMarkup: markup,
                parseMode: ParseMode.MarkdownV2, cancellationToken: CancellationToken.None);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse"))
        {
            msg = await _client.SendMessage(chatId, text, replyMarkup: markup,
                cancellationToken: CancellationToken.None);
        }

        return msg.Id.ToString();
    }

    public async Task EditInteractiveAsync(string chatId, string messageId, UiDocument doc)
    {
        if (_client is null) return;

        var (text, markup) = ((string, InlineKeyboardMarkup))_uiRenderer.Render(doc);

        if (!int.TryParse(messageId, out var msgId)) return;

        try
        {
            await _client.EditMessageText(chatId, msgId, text,
                parseMode: ParseMode.MarkdownV2, cancellationToken: CancellationToken.None);
            await _client.EditMessageReplyMarkup(chatId, msgId, markup,
                cancellationToken: CancellationToken.None);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse"))
        {
            try
            {
                await _client.EditMessageText(chatId, msgId, text,
                    cancellationToken: CancellationToken.None);
                await _client.EditMessageReplyMarkup(chatId, msgId, markup,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception innerEx)
            {
                _logger.LogWarning(innerEx, "Failed to edit interactive message {MsgId}", messageId);
            }
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("message to edit not found") ||
                                              ex.Message.Contains("MESSAGE_ID_INVALID"))
        {
            _logger.LogDebug("Interactive message {MsgId} deleted, sending new message", messageId);
            await SendInteractiveAsync(chatId, doc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to edit interactive message {MsgId}", messageId);
        }
    }

    // ── Callback handling ──

    private async Task HandleCallbackQueryAsync(CallbackQuery query)
    {
        if (query.Data is null) return;
        if (query.Message?.Chat.Id is not { } chatId) return;

        // Fire OnUiCallback
        if (OnUiCallback is null)
        {
            await _client!.AnswerCallbackQuery(query.Id, "Not supported",
                cancellationToken: CancellationToken.None);
            return;
        }

        var callback = ParseCallbackData(query.Data);
        if (callback is null)
        {
            await _client!.AnswerCallbackQuery(query.Id, "Invalid action",
                cancellationToken: CancellationToken.None);
            return;
        }

        // Show a brief loading indicator
        await _client!.AnswerCallbackQuery(query.Id, cancellationToken: CancellationToken.None);

        // Route the callback with chatId so processor can resolve agent context
        var result = await OnUiCallback(chatId.ToString(), callback);

        if (result is not null && query.Message is not null)
        {
            await EditInteractiveAsync(chatId.ToString(), query.Message.Id.ToString(), result);
        }
    }

    private static UiCallback? ParseCallbackData(string data)
    {
        if (string.IsNullOrWhiteSpace(data) || data == "noop")
            return null;

        // Format: "namespace:action:data" — data can contain colons
        var firstColon = data.IndexOf(':');
        if (firstColon < 0) return null;

        var ns = data[..firstColon];
        var rest = data[(firstColon + 1)..];

        var secondColon = rest.IndexOf(':');
        if (secondColon < 0)
        {
            return new UiCallback { Namespace = ns, Action = rest };
        }

        var action = rest[..secondColon];
        var actionData = rest[(secondColon + 1)..];

        return new UiCallback { Namespace = ns, Action = action, Data = actionData };
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
    /// Try sending with MarkdownV2, fall back to plain text (no parseMode) on parse error.
    /// </summary>
    private async Task TryMarkdownAsync(Func<ParseMode, Task> markdownAction, Func<Task> plainAction, CancellationToken ct)
    {
        try
        {
            await markdownAction(ParseMode.MarkdownV2);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("parse"))
        {
            _logger.LogWarning(ex, "MarkdownV2 parse failed, falling back to plain text");
            await plainAction();
        }
    }

    /// <summary>
    /// Edit a message with MarkdownV2, fall back to plain text (no parseMode) on parse error.
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
                await _client!.EditMessageText(chatId, messageId, text, cancellationToken: ct);
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
                    allowedUpdates: new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    cancellationToken: ct);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    if (update.CallbackQuery is { } callbackQuery)
                    {
                        _ = HandleCallbackQueryAsync(callbackQuery);
                        continue;
                    }

                    if (update.Message is not { } message) continue;
                    if (string.IsNullOrWhiteSpace(message.Text)) continue;

                    // Normalize bot-command entities to slash-prefix text for downstream handlers
                    var text = NormalizeBotCommands(message);

                    var inbound = ConvertMessage(message, text);
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

    /// <summary>
    /// Convert BotCommand entities (native Telegram command menu) to slash-prefix text.
    /// Example: BotCommand("new") with text "/new@MyBot" → "/new"
    /// </summary>
    private static string NormalizeBotCommands(Message message)
    {
        var entities = message.Entities ?? Array.Empty<MessageEntity>();
        var text = message.Text ?? "";
        foreach (var entity in entities)
        {
            if (entity.Type != MessageEntityType.BotCommand) continue;
            var raw = text[entity.Offset..(entity.Offset + entity.Length)];
            var atIdx = raw.IndexOf('@');
            return atIdx > 0 ? raw[..atIdx] : raw;
        }
        return text;
    }

    private static InboundMessage? ConvertMessage(Message message, string normalizedText)
    {
        if (message.From is null || string.IsNullOrWhiteSpace(normalizedText))
            return null;

        return new InboundMessage(
            Id: message.MessageId.ToString(),
            ChannelName: "telegram",
            ChatId: message.Chat.Id.ToString(),
            SenderId: message.From.Username ?? message.From.Id.ToString(),
            Text: normalizedText,
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
