using Aether.Agent;
using Aether.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

/// <summary>
/// Background service that manages the WebSocketChannel lifecycle and routes incoming
/// messages through the same pipeline as other channels.
///
/// This is a separate BackgroundService (not bundled into WebSocketChannel itself) to
/// follow the same separation-of-concerns pattern as ChannelMessageProcessor + TelegramChannel:
/// the channel handles transport, the service manages lifecycle and routing.
/// </summary>
public sealed class WebSocketChannelService : BackgroundService
{
    private readonly WebSocketChannel _channel;
    private readonly MessageRouter _router;
    private readonly IServiceProvider _services;
    private readonly ILogger<WebSocketChannelService> _logger;

    public WebSocketChannelService(
        WebSocketChannel channel,
        MessageRouter router,
        IServiceProvider services,
        ILogger<WebSocketChannelService> logger)
    {
        _channel = channel;
        _router = router;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebSocket channel service starting...");

        var tcs = new TaskCompletionSource();
        using var reg = stoppingToken.Register(() => tcs.TrySetResult());

        // Wire incoming messages to the same routing pipeline as other channels
        _channel.OnMessage += (_, message) =>
        {
            _ = Task.Run(() => HandleMessageAsync(message, stoppingToken), stoppingToken);
        };

        try
        {
            await _channel.ConnectAsync(stoppingToken);
            _logger.LogInformation("WebSocket channel connected on ws://0.0.0.0:{Port}/ws", _channel.IsConnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start WebSocket channel");
            return;
        }

        await tcs.Task;

        try
        {
            await _channel.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during WebSocket channel disconnect");
        }
    }

    private async Task HandleMessageAsync(InboundMessage message, CancellationToken ct)
    {
        try
        {
            var routed = await _router.RouteAsync(message, ct);
            if (routed is null) return;

            await _channel.SetTypingAsync(message.ChatId, true, ct);

            using var scope = _services.CreateScope();
            var soul = scope.ServiceProvider.GetRequiredService<AetherSoul>();
            var response = await soul.ProcessAsync(routed.Value.WorkspacePath, routed.Value.Prompt, ct);

            await _channel.SetTypingAsync(message.ChatId, false, ct);
            await _channel.SendMessageAsync(message.ChatId, response.Content, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket message {MessageId} from {ChatId}", message.Id, message.ChatId);
            try
            {
                await _channel.SendMessageAsync(
                    message.ChatId,
                    "Sorry, I encountered an error processing your message.",
                    ct);
            }
            catch { }
        }
    }
}
