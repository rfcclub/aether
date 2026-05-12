using Microsoft.Extensions.Logging;

namespace Aether.Ui;

public class CallbackRouter
{
    private readonly Dictionary<string, IUiCallbackHandler> _handlers;
    private readonly ILogger<CallbackRouter> _logger;

    public CallbackRouter(IEnumerable<IUiCallbackHandler> handlers, ILogger<CallbackRouter> logger)
    {
        _handlers = handlers.ToDictionary(h => h.Namespace, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<UiDocument?> RouteAsync(UiCallback callback, IServiceProvider services, string agentId)
    {
        if (!_handlers.TryGetValue(callback.Namespace, out var handler))
        {
            _logger.LogDebug("No handler registered for callback namespace '{Namespace}'", callback.Namespace);
            return null;
        }

        try
        {
            return await handler.HandleAsync(callback, services, agentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler '{Namespace}' threw for action '{Action}'", callback.Namespace, callback.Action);
            return null;
        }
    }
}
