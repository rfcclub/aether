using Aether.Channels;
using Aether.Config;
using Aether.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Routing;

public sealed class MessageRouter
{
    private readonly AetherDb? _db;
    private readonly ConfigLoader? _configLoader;
    private readonly ChannelMessageQueue? _queue;
    private readonly ILogger<MessageRouter>? _logger;

    private DateTime _lastConfigRead = DateTime.MinValue;
    private Dictionary<string, string>? _bindingCache; // routeKey -> agentName

    public MessageRouter(AetherDb db, ChannelMessageQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    [ActivatorUtilitiesConstructor]
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

        // Handle websocket dynamic routing
        if (routeKey.StartsWith("websocket:websocket:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = routeKey.Split(':');
            if (parts.Length > 3)
            {
                var group = parts[^1];
                var wsBinding = $"websocket:{group}";

                var cache = _bindingCache ?? BuildCacheAndResolveGetCache();
                if (cache.TryGetValue(wsBinding, out var agentName))
                    return agentName;

                if (_configLoader is not null)
                {
                    var config = _configLoader.LoadAsync().Result;
                    foreach (var (name, entry) in config.Agents)
                    {
                        if (entry.Enabled && (string.Equals(name, group, StringComparison.OrdinalIgnoreCase) || 
                                              string.Equals(entry.DisplayName, group, StringComparison.OrdinalIgnoreCase)))
                        {
                            return name;
                        }
                    }
                }
            }
        }

        if (_bindingCache is not null)
            return _bindingCache.TryGetValue(routeKey, out var agentName) ? agentName : ResolveFallback();

        return BuildCacheAndResolve(routeKey);
    }

    private Dictionary<string, string> BuildCacheAndResolveGetCache()
    {
        _bindingCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var agents = _configLoader!.LoadAsync().Result.Agents;

        foreach (var (name, entry) in agents)
        {
            if (!entry.Enabled)
                continue;

            foreach (var binding in entry.Bindings)
            {
                if (!string.IsNullOrEmpty(binding))
                    _bindingCache[binding] = name;
            }
        }

        // Automatically bind tui:local to the default or first enabled agent if not explicitly bound
        if (!_bindingCache.ContainsKey("tui:local"))
        {
            var defaultAgent = agents.Keys.FirstOrDefault(k => string.Equals(k, "default", StringComparison.OrdinalIgnoreCase))
                ?? agents.Keys.FirstOrDefault(k => agents[k].Enabled);
            if (defaultAgent is not null)
            {
                _bindingCache["tui:local"] = defaultAgent;
            }
        }

        _lastConfigRead = DateTime.UtcNow;
        return _bindingCache;
    }

    private string? BuildCacheAndResolve(string routeKey)
    {
        var cache = BuildCacheAndResolveGetCache();

        if (cache.TryGetValue(routeKey, out var matched))
            return matched;

        return ResolveFallback();
    }

    private string? ResolveFallback()
    {
        var agents = _configLoader!.LoadAsync().Result.Agents;

        string? firstCatchAll = null;
        string? defaultAgent = null;

        foreach (var (name, entry) in agents)
        {
            if (!entry.Enabled) continue;
            if (entry.Bindings.Count > 0) continue;

            firstCatchAll ??= name;
            if (string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
                defaultAgent = name;
        }

        return defaultAgent ?? firstCatchAll;
    }

    private void InvalidateCacheIfNeeded()
    {
        var configPath = Path.Combine(_configLoader!.AetherDir, "config.json");
        if (!File.Exists(configPath)) return;

        var lastWrite = File.GetLastWriteTimeUtc(configPath);
        if (lastWrite > _lastConfigRead)
        {
            _bindingCache = null;
        }
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
