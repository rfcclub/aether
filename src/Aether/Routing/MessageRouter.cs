using Aether.Channels;
using Aether.Data;

namespace Aether.Routing;

public sealed class MessageRouter
{
    private readonly AetherDb _db;
    private readonly IMessageQueue _queue;

    public MessageRouter(AetherDb db, IMessageQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<RoutedMessage?> RouteAsync(InboundMessage message, CancellationToken ct = default)
    {
        if (message.IsFromBot)
        {
            return null;
        }

        var route = await _db.GetGroupRouteAsync(message.RouteKey, ct);
        if (route is null)
        {
            return null;
        }

        var prompt = NormalizePrompt(message.Text, route.Value);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var routed = new RoutedMessage(message, route.Value.Folder, prompt);
        await _queue.EnqueueAsync(routed, ct);
        return routed;
    }

    private static string? NormalizePrompt(string text, GroupRoute route)
    {
        var prompt = text.Trim();
        if (route.Trigger is null)
        {
            return prompt;
        }

        if (!prompt.StartsWith(route.Trigger, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return prompt[route.Trigger.Length..].Trim();
    }
}
