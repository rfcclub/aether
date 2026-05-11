using System.Reflection;
using System.Text.Json;
using Aether.Channels;
using Aether.Scheduling;
using Aether.Skills;
using Aether.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins;

public class PluginLoader
{
    private readonly string _pluginsPath;
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(string pluginsPath, ILogger<PluginLoader>? logger = null)
    {
        _pluginsPath = pluginsPath;
        _logger = logger ?? NullLogger<PluginLoader>.Instance;
    }

    public async Task<PluginLoadResult> LoadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_pluginsPath))
        {
            _logger.LogInformation("Plugins directory '{Path}' not found — no plugins loaded", _pluginsPath);
            return Empty;
        }

        var manifestDirs = new List<(PluginManifest Manifest, string Dir)>();
        foreach (var dir in Directory.GetDirectories(_pluginsPath))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;

            var manifest = await TryParseManifestAsync(manifestPath, ct);
            if (manifest is not null)
                manifestDirs.Add((manifest, dir));
        }

        if (manifestDirs.Count == 0)
        {
            _logger.LogInformation("No valid plugins found in '{Path}'", _pluginsPath);
            return Empty;
        }

        var ordered = TopologicalSort(manifestDirs);
        return await LoadPluginsAsync(ordered, ct);
    }

    private async Task<PluginLoadResult> LoadPluginsAsync(
        List<(PluginManifest Manifest, string Dir)> ordered,
        CancellationToken ct)
    {
        var hooks = new List<IHook>();
        var tools = new List<IToolImplementation>();
        var channels = new List<IChannel>();
        var skillProviders = new List<ISkillProvider>();
        var cronProviders = new List<ICronTaskProvider>();
        var lifecycleHandlers = new List<IPluginLifecycle>();
        var manifests = new List<PluginManifest>();

        foreach (var (manifest, dir) in ordered)
        {
            ct.ThrowIfCancellationRequested();
            manifests.Add(manifest);

            _logger.LogInformation("Loading plugin '{PluginName}' v{Version} from {Dir}",
                manifest.Name, manifest.Version, dir);

            try
            {
                if (!string.IsNullOrEmpty(manifest.Assembly))
                {
                    var asmPath = Path.Combine(dir, manifest.Assembly);
                    if (File.Exists(asmPath))
                    {
                        var alc = new IsolatedPluginLoadContext(dir, manifest.Name);
                        var assembly = alc.LoadFromAssemblyPath(asmPath);
                        DiscoverTypes(assembly, hooks, tools, channels, skillProviders, cronProviders, lifecycleHandlers);
                        _logger.LogInformation("Plugin '{PluginName}' assembly loaded: {AsmPath}", manifest.Name, asmPath);
                    }
                    else
                    {
                        _logger.LogWarning("Plugin '{PluginName}' assembly not found: {AsmPath}", manifest.Name, asmPath);
                    }
                }

                _logger.LogInformation("Plugin '{PluginName}' loaded successfully", manifest.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin '{PluginName}' — skipping", manifest.Name);
            }
        }

        return new PluginLoadResult(hooks, tools, channels, skillProviders, cronProviders, lifecycleHandlers, manifests);
    }

    private static void DiscoverTypes(
        Assembly assembly,
        List<IHook> hooks,
        List<IToolImplementation> tools,
        List<IChannel> channels,
        List<ISkillProvider> skillProviders,
        List<ICronTaskProvider> cronProviders,
        List<IPluginLifecycle> lifecycleHandlers)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            try
            {
                if (typeof(IHook).IsAssignableFrom(type))
                    hooks.Add((IHook)Activator.CreateInstance(type)!);

                if (typeof(IToolImplementation).IsAssignableFrom(type))
                    tools.Add((IToolImplementation)Activator.CreateInstance(type)!);

                if (typeof(IChannel).IsAssignableFrom(type))
                    channels.Add((IChannel)Activator.CreateInstance(type)!);

                if (typeof(ISkillProvider).IsAssignableFrom(type))
                    skillProviders.Add((ISkillProvider)Activator.CreateInstance(type)!);

                if (typeof(ICronTaskProvider).IsAssignableFrom(type))
                    cronProviders.Add((ICronTaskProvider)Activator.CreateInstance(type)!);

                if (typeof(IPluginLifecycle).IsAssignableFrom(type))
                    lifecycleHandlers.Add((IPluginLifecycle)Activator.CreateInstance(type)!);
            }
            catch (Exception ex)
            {
                // Logging is handled by caller
                System.Diagnostics.Debug.WriteLine($"Failed to instantiate {type.FullName}: {ex.Message}");
            }
        }
    }

    private List<(PluginManifest Manifest, string Dir)> TopologicalSort(
        List<(PluginManifest Manifest, string Dir)> plugins)
    {
        var sorted = new List<(PluginManifest, string)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (manifest, dir) in plugins)
        {
            if (!visited.Contains(manifest.Name))
                Visit(manifest, dir, plugins, sorted, visited, visiting);
        }

        return sorted;
    }

    private void Visit(
        PluginManifest manifest,
        string dir,
        List<(PluginManifest Manifest, string Dir)> allPlugins,
        List<(PluginManifest, string)> sorted,
        HashSet<string> visited,
        HashSet<string> visiting)
    {
        if (visiting.Contains(manifest.Name))
        {
            _logger.LogError("Circular dependency detected for plugin '{PluginName}' — skipping", manifest.Name);
            return;
        }

        if (visited.Contains(manifest.Name)) return;

        visiting.Add(manifest.Name);

        if (manifest.Dependencies is not null)
        {
            foreach (var (depName, _) in manifest.Dependencies)
            {
                var dep = allPlugins.FirstOrDefault(p =>
                    string.Equals(p.Manifest.Name, depName, StringComparison.OrdinalIgnoreCase));
                if (dep.Manifest is not null)
                    Visit(dep.Manifest, dep.Dir, allPlugins, sorted, visited, visiting);
                else
                    _logger.LogWarning("Plugin '{PluginName}' depends on '{DepName}' which is not installed",
                        manifest.Name, depName);
            }
        }

        visiting.Remove(manifest.Name);
        visited.Add(manifest.Name);
        sorted.Add((manifest, dir));
    }

    private static async Task<PluginManifest?> TryParseManifestAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name))
                return null;

            return manifest;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse plugin manifest '{path}': {ex.Message}");
            return null;
        }
    }

    private static readonly PluginLoadResult Empty = new(
        Array.Empty<IHook>(),
        Array.Empty<IToolImplementation>(),
        Array.Empty<IChannel>(),
        Array.Empty<ISkillProvider>(),
        Array.Empty<ICronTaskProvider>(),
        Array.Empty<IPluginLifecycle>(),
        Array.Empty<PluginManifest>());
}
