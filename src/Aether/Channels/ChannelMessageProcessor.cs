using System.Text;
using Aether.Agent;
using Aether.Config;
using Aether.Providers;
using Aether.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

public sealed class ChannelMessageProcessor : BackgroundService
{
    /// <summary>
    /// Maximum number of streaming chunks to send to the channel individually.
    /// After this threshold, chunks are batched to avoid rate limits / excessive edits.
    /// </summary>
    private const int MaxStreamingEdits = 50;

    private readonly IChannel _channel;
    private readonly MessageRouter _router;
    private readonly IServiceProvider _services;
    private readonly ChannelAccess _channelAccess;
    private readonly ConfigLoader _configLoader;
    private readonly ILogger<ChannelMessageProcessor> _logger;

    public ChannelMessageProcessor(
        IChannel channel,
        MessageRouter router,
        IServiceProvider services,
        ChannelAccess channelAccess,
        ConfigLoader configLoader,
        ILogger<ChannelMessageProcessor> logger)
    {
        _channel = channel;
        _router = router;
        _services = services;
        _channelAccess = channelAccess;
        _configLoader = configLoader;
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
            // Access control — gate before routing
            var access = await _channelAccess.CheckAccessAsync(message.SenderId, ct);
            switch (access)
            {
                case AccessResult.Denied:
                    return; // silently drop

                case AccessResult.NeedsPairing:
                    var code = await _channelAccess.RequestPairingAsync(message.SenderId, ct);
                    await _channel.SendMessageAsync(message.ChatId,
                        $"🔐 This bot is private.\n\nYour pairing code: **{code}**\n\nAsk the bot owner to run:\n`aether pair {code}`",
                        ct);
                    return;

                case AccessResult.Allowed:
                    break; // proceed
            }

            var routed = await _router.RouteAsync(message, ct);
            if (routed is null) return;

            await _channel.SetTypingAsync(message.ChatId, true, ct);

            using var scope = _services.CreateScope();

            // Apply per-agent provider config before LLM call
            var agentSpec = _configLoader.GetAgentSpec(routed.Value.AgentName);
            if (agentSpec is not null)
            {
                var providerRouter = scope.ServiceProvider.GetService<ProviderRouter>();
                if (providerRouter is not null)
                    providerRouter.CurrentAgent = agentSpec;
            }

            var soul = scope.ServiceProvider.GetRequiredService<AetherSoul>();

            // Stream the response through the channel, accumulating for the final save
            var fullResponse = new StringBuilder();
            var chunkIndex = 0;
            var editsRemaining = MaxStreamingEdits;

            await foreach (var chunk in soul.ProcessStreamingAsync(routed.Value.WorkspacePath, routed.Value.Prompt, ct))
            {
                fullResponse.Append(chunk);

                // Only push to channel for the first N chunks to avoid rate limits.
                // After that, just accumulate silently.
                if (editsRemaining > 0)
                {
                    await _channel.SendStreamingChunkAsync(
                        message.ChatId,
                        fullResponse.ToString(), // send accumulated text so edits show full progress
                        chunkIndex,
                        ct);
                    chunkIndex++;
                    editsRemaining--;
                }
            }

            await _channel.SetTypingAsync(message.ChatId, false, ct);

            // Signal the stream is complete with the full accumulated text
            await _channel.SendStreamingCompleteAsync(message.ChatId, fullResponse.ToString(), ct);
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
