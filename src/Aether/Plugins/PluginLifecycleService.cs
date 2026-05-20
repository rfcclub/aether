using Aether.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins;

public sealed class PluginLifecycleService : IHostedService
{
    private readonly PluginLoader _loader;
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginLifecycleService> _logger;
    private readonly List<IPluginLifecycle> _lifecycles = new();

    public PluginLifecycleService(
        PluginLoader loader,
        IServiceProvider services,
        ILogger<PluginLifecycleService> logger)
    {
        _loader = loader;
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting plugin lifecycles...");
        var (result, manifestPairs) = await _loader.LoadAllAsync(ct);

        var pluginsPath = _loader.GetType().GetField("_pluginsPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_loader) as string ?? "plugins";
        pluginsPath = Path.GetFullPath(pluginsPath);

        // Simplification: Load all lifecycles in result
        foreach (var lifecycle in result.LifecycleHandlers)
        {
            try
            {
                _logger.LogInformation("Initializing lifecycle for {LifecycleType}", lifecycle.GetType().Name);
                await lifecycle.OnLoadAsync(new PluginContext
                {
                    PluginDirectory = pluginsPath,
                    Logger = _services.GetService<ILoggerFactory>()?.CreateLogger(lifecycle.GetType()) ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                    Services = _services,
                    Manifest = new PluginManifest { Name = lifecycle.GetType().Namespace ?? "plugin" }
                }, ct);
                _lifecycles.Add(lifecycle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize plugin lifecycle {LifecycleType}", lifecycle.GetType().Name);
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Stopping plugin lifecycles...");
        foreach (var lifecycle in _lifecycles)
        {
            try
            {
                await lifecycle.OnUnloadAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during plugin lifecycle unload {LifecycleType}", lifecycle.GetType().Name);
            }
        }
    }
}
