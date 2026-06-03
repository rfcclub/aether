using System.Text;
using Aether.Agent;
using Aether.Agents;
using Aether.Config;
using Aether.Memory;
using Aether.Plugins;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Aether.Ui;
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
    private readonly HookEngine? _hooks;
    private readonly CallbackRouter? _callbackRouter;
    private readonly SessionManager _sessionManager;
    private readonly SessionCompactionService? _compactionService;
    private readonly ILogger<ChannelMessageProcessor> _logger;

    public ChannelMessageProcessor(
        IChannel channel,
        MessageRouter router,
        IServiceProvider services,
        ChannelAccess channelAccess,
        ConfigLoader configLoader,
        SlashCommandHandler slashCommands,
        FileMemory memory,
        SessionManager sessionManager,
        ILogger<ChannelMessageProcessor> logger,
        HookEngine? hooks = null,
        CallbackRouter? callbackRouter = null,
        SessionCompactionService? compactionService = null)
    {
        _channel = channel;
        _router = router;
        _services = services;
        _channelAccess = channelAccess;
        _configLoader = configLoader;
        _slashCommands = slashCommands;
        _memory = memory;
        _sessionManager = sessionManager;
        _hooks = hooks;
        _callbackRouter = callbackRouter;
        _logger = logger;
        _compactionService = compactionService;
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

        // Wire interactive callback routing
        if (_callbackRouter is not null)
        {
            _channel.OnUiCallback += async (chatId, callback) =>
            {
                // Resolve agent from chat via an empty inbound message route lookup
                // We create a minimal message just to resolve the agent binding
                var dummy = new InboundMessage("", _channel.Name, chatId, "", "", DateTime.MinValue, false);
                var routed = await _router.RouteAsync(dummy, CancellationToken.None);
                var agentId = routed?.AgentName ?? "";

                return await _callbackRouter.RouteAsync(callback, _services, agentId);
            };
        }

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
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SessionCancellationRegistry.Register(message.ChatId, sessionCts);

        try
        {
            var linkedToken = sessionCts.Token;

            // ── OnMessageReceived hook ──
            if (_hooks is not null)
            {
                var msgCtx = new OnMessageReceivedContext
                {
                    ChatId = message.ChatId,
                    SenderId = message.SenderId,
                    ChannelName = _channel.Name,
                    Text = message.Text,
                    WorkspacePath = ""
                };
                var msgResult = await _hooks.RunAsync(HookPoint.OnMessageReceived, msgCtx, linkedToken);
                if (!msgResult.Success || msgCtx.Dropped)
                    return;
                if (msgCtx.OverrideText is not null)
                    message = message with { Text = msgCtx.OverrideText };
            }

            // Access control — gate before routing
            var access = await _channelAccess.CheckAccessAsync(message.SenderId, linkedToken);
            switch (access)
            {
                case AccessResult.Denied:
                    return; // silently drop

                case AccessResult.NeedsPairing:
                    var code = await _channelAccess.RequestPairingAsync(message.SenderId, linkedToken);
                    await _channel.SendMessageAsync(message.ChatId,
                        $"🔐 This bot is private.\n\nYour pairing code: **{code}**\n\nAsk the bot owner to run:\n`aether pair {code}`",
                        linkedToken);
                    return;

                case AccessResult.Allowed:
                    break; // proceed
            }

            var routed = await _router.RouteAsync(message, linkedToken);
            if (routed is null) return;

            HookEngine? requestHooks = _hooks;
            if (_hooks is not null)
            {
                var routedCtx = new OnMessageRoutedContext
                {
                    AgentName = routed.Value.AgentName,
                    WorkspacePath = routed.Value.WorkspacePath,
                    SessionId = "",
                    Message = message,
                    ResolvedAgentName = routed.Value.AgentName
                };
                var routedResult = await _hooks.RunAsync(HookPoint.OnMessageRouted, routedCtx, linkedToken);
                if (!routedResult.Success)
                    return;
                if (routedCtx.RerouteToAgent && routedCtx.RerouteAgentName is not null)
                    routed = routed.Value with { AgentName = routedCtx.RerouteAgentName };
            }

            var agentSpec = _configLoader.GetAgentSpec(routed.Value.AgentName);
            var agentConfig = _configLoader.GetAgentConfig(routed.Value.AgentName);
            if (agentSpec?.Plugins is not null && _hooks is not null)
                requestHooks = _hooks.FilterForAgent(agentSpec.Plugins);

            // Slash commands — handle before LLM scope
            var slashCtx = new SlashCommandContext(
                message.Text, routed.Value.AgentName, routed.Value.WorkspacePath, _services);
            var slashResult = await _slashCommands.HandleAsync(slashCtx, linkedToken);
            if (slashResult is not null && !slashResult.AutoGreet)
            {
                if (slashResult.InteractiveUi is not null)
                {
                    await _channel.SendInteractiveAsync(message.ChatId, slashResult.InteractiveUi);
                }
                else
                {
                    await _channel.SendMessageAsync(message.ChatId, slashResult.Text, linkedToken);
                }
                return;
            }

            // Build prompt — inject startup sequence for /new, /reset, or pending reset
            var prompt = routed.Value.Prompt;
            if (slashResult is { AutoGreet: true })
            {
                await _channel.SendMessageAsync(message.ChatId, slashResult.Text, linkedToken);
                prompt = "A new session was started via /new or /reset. " +
                         "Execute your Session Startup sequence now — " +
                         "read the required files, then greet the user warmly.";
            }
            else if (SlashCommandHandler.PendingSessionReset.TryRemove(routed.Value.AgentName, out _))
            {
                prompt = "A new session was started via /new or /reset. " +
                         "Execute your Session Startup sequence now — " +
                         "read the required files before responding to the user.\n\n" +
                         prompt;
            }

            // Add user message to ephemeral context
            _memory.AddToContext($"User: {message.Text}", 0.5f);

            await _channel.SetTypingAsync(message.ChatId, true, linkedToken);

            using var scope = _services.CreateScope();

            // Apply per-agent provider config before LLM call
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

            var soul = requestHooks == _hooks
                ? scope.ServiceProvider.GetRequiredService<AetherSoul>()
                : new AetherSoul(
                    scope.ServiceProvider.GetRequiredService<ProviderRouter>(),
                    scope.ServiceProvider.GetRequiredService<Aether.Tooling.ToolExecutor>(),
                    scope.ServiceProvider.GetRequiredService<Tooling.ToolRegistry>(),
                    scope.ServiceProvider.GetRequiredService<AgentProfile>(),
                    scope.ServiceProvider.GetRequiredService<ILogger<AetherSoul>>(),
                    requestHooks,
                    scope.ServiceProvider.GetRequiredService<SqliteMemorySystem>(),
                    scope.ServiceProvider.GetRequiredService<SessionManager>());

            await _channel.SetTypingAsync(message.ChatId, true, linkedToken);

            var fullResponse = new StringBuilder();
            int chunkIndex = 0;
            await foreach (var chunk in soul.ProcessStreamingAsync(routed.Value.WorkspacePath, prompt, linkedToken))
            {
                fullResponse.Append(chunk);
                await _channel.SendStreamingChunkAsync(message.ChatId, chunk, chunkIndex++, linkedToken);
            }

            // Add assistant response to ephemeral context
            var responseText = fullResponse.ToString();
            _memory.AddToContext($"Assistant: {responseText}", 0.5f);

            // ── OnMessageSent hook ──
            if (requestHooks is not null)
            {
                var sentCtx = new OnMessageSentContext
                {
                    ChatId = message.ChatId,
                    Text = responseText,
                    AgentName = routed.Value.AgentName,
                    WorkspacePath = routed.Value.WorkspacePath
                };
                await requestHooks.RunAllAsync(HookPoint.OnMessageSent, sentCtx, linkedToken);
                if (sentCtx.Suppress)
                {
                    await _channel.SetTypingAsync(message.ChatId, false, linkedToken);
                    return;
                }
                if (sentCtx.OverrideText is not null)
                    responseText = sentCtx.OverrideText;
            }

            await _channel.SetTypingAsync(message.ChatId, false, linkedToken);
            await _channel.SendStreamingCompleteAsync(message.ChatId, responseText, linkedToken);

            // Trigger Tier 3 Compaction if history exceeds critical threshold
            if (_compactionService is not null)
            {
                try
                {
                    var session = await _sessionManager.GetOrCreateSessionAsync(routed.Value.WorkspacePath, linkedToken);
                    // Fetch recent history just to get the length/token estimate
                    var recentHistory = await _sessionManager.GetHistoryAsync(session.Id, 20000, linkedToken); 
                    if (recentHistory.Count > 50) // e.g., > 50 messages
                    {
                        _compactionService.EnqueueCompaction(session.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to evaluate session compaction.");
                }
            }
        }
        catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
        {
            _logger.LogInformation("Message handling task was cancelled by user action for chat: {ChatId}", message.ChatId);
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
        finally
        {
            SessionCancellationRegistry.Remove(message.ChatId);
        }
    }
}
