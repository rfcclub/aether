using System.Threading.Channels;
using Aether.Agent;
using Aether.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

public sealed class ChannelMessageProcessor : BackgroundService
{
    private readonly IChannel _channel;
    private readonly MessageRouter _router;
    private readonly IServiceProvider _services;
    private readonly ILogger<ChannelMessageProcessor> _logger;

    public ChannelMessageProcessor(
        IChannel channel,
        MessageRouter router,
        IServiceProvider services,
        ILogger<ChannelMessageProcessor> logger)
    {
        _channel = channel;
        _router = router;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Channel message processor starting...");

        var tcs = new TaskCompletionSource();
        using var reg = stoppingToken.Register(() => tcs.TrySetResult());

        _channel.OnMessage += (_, message) =>
        {
            _ = Task.Run(() => HandleMessageAsync(message, stoppingToken), stoppingToken);
        };

        try
        {
            await _channel.ConnectAsync(stoppingToken);
            _logger.LogInformation("Channel {ChannelName} connected", _channel.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect channel {ChannelName}", _channel.Name);
            return;
        }

        await tcs.Task;

        try
        {
            await _channel.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during channel disconnect");
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
            var response = await soul.ProcessAsync(routed.Value.GroupFolder, routed.Value.Prompt, ct);

            await _channel.SetTypingAsync(message.ChatId, false, ct);
            await _channel.SendMessageAsync(message.ChatId, response.Content, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId} from {ChatId}", message.Id, message.ChatId);
            try
            {
                await _channel.SendMessageAsync(message.ChatId, "Sorry, I encountered an error processing your message.", ct);
            }
            catch { }
        }
    }
}
