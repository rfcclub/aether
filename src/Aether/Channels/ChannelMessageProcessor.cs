using System.Text;
using Aether.Agent;
using Aether.Config;
using Aether.Memory;
using Aether.Providers;
using Aether.Routing;
using Microsoft.Extensions.Configuration;
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
    private readonly SlashCommandHandler _slashCommands;
    private readonly FileMemory _memory;
    private readonly ILogger<ChannelMessageProcessor> _logger;

    public ChannelMessageProcessor(
        IChannel channel,
        MessageRouter router,
        IServiceProvider services,
        ChannelAccess channelAccess,
        ConfigLoader configLoader,
        SlashCommandHandler slashCommands,
        FileMemory memory,
        ILogger<ChannelMessageProcessor> logger)
    {
        _channel = channel;
        _router = router;
        _services = services;
        _channelAccess = channelAccess;
        _configLoader = configLoader;
        _slashCommands = slashCommands;
        _memory = memory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Channel message processor starting...");

        await _channelAccess.LoadAsync(stoppingToken);
        _logger.LogInformation("Channel access loaded");

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

            // Slash commands — handle before LLM scope
            var slashCtx = new SlashCommandContext(
                message.Text, routed.Value.AgentName, routed.Value.WorkspacePath, _services);
            var slashResult = await _slashCommands.HandleAsync(slashCtx, ct);
            if (slashResult is not null)
            {
                await _channel.SendMessageAsync(message.ChatId, slashResult.Text, ct);
                return;
            }

            // Add user message to ephemeral context
            _memory.AddToContext($"User: {message.Text}", 0.5f);

            await _channel.SetTypingAsync(message.ChatId, true, ct);

            using var scope = _services.CreateScope();

            // Apply per-agent provider config before LLM call
            var agentSpec = _configLoader.GetAgentSpec(routed.Value.AgentName);
            var agentConfig = _configLoader.GetAgentConfig(routed.Value.AgentName);
            if (agentSpec is not null)
            {
                var providerRouter = scope.ServiceProvider.GetService<ProviderRouter>();
                if (providerRouter is not null)
                {
                    providerRouter.CurrentAgent = agentSpec;

                    // Set model chain for model-first routing
                    if (agentConfig?.Model is { } modelCfg)
                    {
                        var chain = new List<string>();
                        if (!string.IsNullOrEmpty(modelCfg.Primary))
                            chain.Add(modelCfg.Primary);
                        chain.AddRange(modelCfg.Fallbacks);
                        if (chain.Count > 0)
                            providerRouter.ModelChain = chain;
                    }
                }

                var toolExecutor = scope.ServiceProvider.GetService<Aether.Agent.ToolExecutor>();
                if (toolExecutor is not null)
                    toolExecutor.SetAgentContext(routed.Value.WorkspacePath, agentSpec.Tools);

                // Set sandbox context for tool dispatch
                var sandboxType = scope.ServiceProvider.GetRequiredService<IConfiguration>()["sandbox:type"] ?? "process";
                Tooling.ToolSandboxAccessor.Current = new Tooling.SandboxContext(
                    routed.Value.WorkspacePath, agentSpec.Tools, sandboxType);
            }

            var soul = scope.ServiceProvider.GetRequiredService<AetherSoul>();

            await _channel.SetTypingAsync(message.ChatId, true, ct);

            var fullResponse = new StringBuilder();
            await foreach (var chunk in soul.ProcessStreamingAsync(routed.Value.WorkspacePath, routed.Value.Prompt, ct))
            {
                fullResponse.Append(chunk);
            }

            // Add assistant response to ephemeral context
            var responseText = fullResponse.ToString();
            _memory.AddToContext($"Assistant: {responseText}", 0.5f);

            await _channel.SetTypingAsync(message.ChatId, false, ct);
            await _channel.SendMessageAsync(message.ChatId, responseText, ct);
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
