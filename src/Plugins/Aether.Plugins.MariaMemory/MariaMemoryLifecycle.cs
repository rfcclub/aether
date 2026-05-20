using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class MariaMemoryLifecycle : IPluginLifecycle
{
    public static IServiceProvider? Services { get; private set; }
    public static MariaMemoryStore? Store { get; private set; }
    public static ContextAssemblyEngine? ContextEngine { get; private set; }
    public static DreamingService? Dreamer { get; private set; }
    public static MariaMemoryApi? Api { get; private set; }
    public static ResearchLinker? Linker { get; private set; }

    private PluginContext? _context;
    private ILogger? _logger;

    public Task OnLoadAsync(PluginContext context, CancellationToken ct)
    {
        _context = context;
        Services = context.Services;
        _logger = context.Logger;

        // Initialize Store (Dual-Write SQLite + JSONL)
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var defaultWorkspace = Path.Combine(aetherHome, "workspaces", "default");

        Store = new MariaMemoryStore(defaultWorkspace, _logger);
        var sqliteStore = new MariaSqliteStore(defaultWorkspace, _logger);
        var promoter = new AutoPromotionEngine(sqliteStore, defaultWorkspace, _logger);
        ContextEngine = new ContextAssemblyEngine(Store, _logger);

        Linker = new ResearchLinker(defaultWorkspace, sqliteStore, _logger);

        Dreamer = new DreamingService(Store, promoter, _logger);
        Dreamer.StartBackground(TimeSpan.FromHours(1)); // Run every hour

        Api = new MariaMemoryApi(Store, ContextEngine, Dreamer, _logger);
        Api.Start();

        _logger.LogInformation("MariaMemoryPlugin v2.0 (The 7 Gems) loaded.");
        return Task.CompletedTask;
    }
    public Task OnUnloadAsync(CancellationToken ct)
    {
        _logger?.LogInformation("MariaMemoryPlugin unloading");
        Dreamer?.Stop();
        Api?.Stop();
        return Task.CompletedTask;
    }

    public Task OnAgentEnabledAsync(string agentName, CancellationToken ct)
    {
        _logger?.LogInformation("MariaMemoryPlugin enabled for agent: {AgentName}", agentName);
        return Task.CompletedTask;
    }
}
