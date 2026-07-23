using System.Reflection;
using System.Runtime.Loader;

namespace Aether.Plugins;

public sealed class IsolatedPluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDir;

    public IsolatedPluginLoadContext(string pluginDir, string pluginName)
        : base(pluginName, isCollectible: true)
    {
        _pluginDir = pluginDir;
        _resolver = new AssemblyDependencyResolver(pluginDir);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // Force sharing of the core Aether assembly by forwarding to the default context
        if (name.Name == "Aether")
            return null;

        // Plugin's own dependencies resolved from its directory first
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path is not null)
            return LoadFromAssemblyPath(path);

        // Fall back to default context for shared Aether assemblies
        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is not null)
            return LoadUnmanagedDllFromPath(path);

        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
