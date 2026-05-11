using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins;

/// <summary>
/// Wraps the real IServiceProvider and enforces capability-based service access.
/// Only services declared in the plugin manifest's permissions.services are resolved.
/// Unlisted services return null with a warning log.
/// </summary>
public sealed class PluginPermissionGate : IServiceProvider
{
    private readonly IServiceProvider _inner;
    private readonly PluginManifest _manifest;
    private readonly ILogger<PluginPermissionGate> _logger;

    private readonly HashSet<string> _allowedServices;

    public PluginPermissionGate(
        IServiceProvider inner,
        PluginManifest manifest,
        ILogger<PluginPermissionGate>? logger = null)
    {
        _inner = inner;
        _manifest = manifest;
        _logger = logger ?? NullLogger<PluginPermissionGate>.Instance;
        var declared = manifest.Permissions?.Services;
        _allowedServices = new HashSet<string>(
            declared as IEnumerable<string> ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public object? GetService(Type serviceType)
    {
        var name = serviceType.Name;

        if (!_allowedServices.Contains(name))
        {
            _logger.LogWarning("Plugin '{PluginName}' attempted to access undeclared service '{ServiceName}'",
                _manifest.Name, name);
            return null;
        }

        _logger.LogDebug("Plugin '{PluginName}' resolved service '{ServiceName}'",
            _manifest.Name, name);

        return _inner.GetService(serviceType);
    }
}
