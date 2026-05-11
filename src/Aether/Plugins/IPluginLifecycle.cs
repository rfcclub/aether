using Microsoft.Extensions.Logging;

namespace Aether.Plugins;

public interface IPluginLifecycle
{
    Task OnLoadAsync(PluginContext context, CancellationToken ct);
    Task OnUnloadAsync(CancellationToken ct);
    Task OnAgentEnabledAsync(string agentName, CancellationToken ct) => Task.CompletedTask;
    Task OnAgentDisabledAsync(string agentName, CancellationToken ct) => Task.CompletedTask;
}

public class PluginContext
{
    public string PluginName { get; init; } = "";
    public string PluginDirectory { get; init; } = "";
    public PluginManifest Manifest { get; init; } = null!;
    public IServiceProvider Services { get; init; } = null!;
    public ILogger Logger { get; init; } = null!;
    public Dictionary<string, object?> Config { get; init; } = new();
}
