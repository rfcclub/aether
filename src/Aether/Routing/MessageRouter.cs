using Aether.Channels;
using Aether.Config;
using Aether.Data;
using Microsoft.Extensions.Logging;

namespace Aether.Routing;

public sealed class MessageRouter
{
    private readonly AetherDb? _db;
    private readonly ConfigLoader? _configLoader;
    private readonly IMessageQueue? _queue;
    private readonly ILogger<MessageRouter>? _logger;

    private DateTime _lastConfigRead = DateTime.MinValue;
    private Dictionary<string, string>? _bindingCache; // routeKey -> agentName

    public MessageRouter(AetherDb db, IMessageQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public MessageRouter(ConfigLoader configLoader, ILogger<MessageRouter> logger)
    {
        _configLoader = configLoader;
        _logger = logger;
    }

    public async Task<RoutedMessage?> RouteAsync(InboundMessage message, CancellationToken ct = default)
    {
        if (message.IsFromBot)
            return null;

        // Try binding-based routing first
        if (_configLoader is not null)
        {
            var agentName = ResolveAgentFromBindings(message.RouteKey);
            if (agentName is not null)
            {
                var agentConfig = _configLoader.GetAgentConfig(agentName);
                if (agentConfig is not null)
                {
                    var workspace = agentConfig.Workspace ?? "";
                    var prompt = message.Text.Trim();
                    if (string.IsNullOrWhiteSpace(prompt))
                        return null;

                    return new RoutedMessage(message, agentName, workspace, prompt);
                }
            }

            // No binding matched — message not routable
            return null;
        }

        // Legacy AetherDb-based routing
        if (_db is not null && _queue is not null)
        {
            var route = await _db.GetGroupRouteAsync(message.RouteKey, ct);
            if (route is null)
                return null;

            var prompt = NormalizePrompt(message.Text, route.Value);
            if (string.IsNullOrWhiteSpace(prompt))
                return null;

            var routed = new RoutedMessage(message, route.Value.Folder, route.Value.Folder, prompt);
            await _queue.EnqueueAsync(routed, ct);
            return routed;
        }

        return null;
    }

    private string? ResolveAgentFromBindings(string routeKey)
    {
        InvalidateCacheIfNeeded();
        if (_bindingCache is not null)
            return _bindingCache.TryGetValue(routeKey, out var agentName) ? agentName : ResolveFallback();

        return BuildCacheAndResolve(routeKey);
    }

    private string? BuildCacheAndResolve(string routeKey)
    {
        _bindingCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var agents = _configLoader!.LoadAsync().Result.Agents;

        string? firstEnabled = null;
        string? defaultAgent = null;

        foreach (var (name, entry) in agents)
        {
            if (!entry.Enabled)
                continue;

            firstEnabled ??= name;

            if (string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
                defaultAgent = name;

            if (entry.Bindings is not null)
            {
                foreach (var binding in entry.Bindings)
                {
                    if (!string.IsNullOrEmpty(binding))
                        _bindingCache[binding] = name;
                }
            }
        }

        _lastConfigRead = DateTime.UtcNow;

        if (_bindingCache.TryGetValue(routeKey, out var matched))
            return matched;

        return defaultAgent ?? firstEnabled;
    }

    private string? ResolveFallback()
    {
        var agents = _configLoader!.LoadAsync().Result.Agents;

        foreach (var (name, entry) in agents)
        {
            if (!entry.Enabled) continue;
            if (string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
                return name;
        }

        foreach (var (name, entry) in agents)
        {
            if (entry.Enabled) return name;
        }

        return null;
    }

    private void InvalidateCacheIfNeeded()
    {
        // Cache invalidation is checked per-request — simple approach for now
    }

    private static string? NormalizePrompt(string text, GroupRoute route)
    {
        var prompt = text.Trim();
        if (route.Trigger is null)
            return prompt;

        if (!prompt.StartsWith(route.Trigger, StringComparison.OrdinalIgnoreCase))
            return null;

        return prompt[route.Trigger.Length..].Trim();
    }
}
