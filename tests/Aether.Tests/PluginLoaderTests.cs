using System.Text.Json;
using Aether.Plugins;

namespace Aether.Tests;

public class PluginLoaderTests
{
    private static string CreateTempPluginsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aether-test-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task ManifestParsing_LoadsValidManifest()
    {
        var pluginsDir = CreateTempPluginsDir();
        try
        {
            var pluginDir = Path.Combine(pluginsDir, "test-plugin");
            Directory.CreateDirectory(pluginDir);
            var manifest = new PluginManifest { Name = "test-plugin", Version = "1.0.0", Description = "A test plugin" };
            var json = JsonSerializer.Serialize(manifest);
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), json);

            var loader = new PluginLoader(pluginsDir);
            var result = await loader.LoadAllAsync();

            Assert.Single(result.Manifests);
            Assert.Equal("test-plugin", result.Manifests[0].Name);
            Assert.Equal("1.0.0", result.Manifests[0].Version);
        }
        finally
        {
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingNameField_SkipsPlugin()
    {
        var pluginsDir = CreateTempPluginsDir();
        try
        {
            var pluginDir = Path.Combine(pluginsDir, "bad-plugin");
            Directory.CreateDirectory(pluginDir);
            await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"),
                """{"version": "1.0.0"}""");

            var loader = new PluginLoader(pluginsDir);
            var result = await loader.LoadAllAsync();

            Assert.Empty(result.Manifests);
        }
        finally
        {
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task DirectoryWithoutManifest_Skipped()
    {
        var pluginsDir = CreateTempPluginsDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginsDir, "not-a-plugin"));

            var loader = new PluginLoader(pluginsDir);
            var result = await loader.LoadAllAsync();

            Assert.Empty(result.Manifests);
        }
        finally
        {
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyPluginsDirectory_ReturnsEmpty()
    {
        var pluginsDir = CreateTempPluginsDir();
        try
        {
            var loader = new PluginLoader(pluginsDir);
            var result = await loader.LoadAllAsync();

            Assert.Empty(result.Hooks);
            Assert.Empty(result.Manifests);
        }
        finally
        {
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task MissingPluginsDirectory_ReturnsEmpty()
    {
        var loader = new PluginLoader("/nonexistent/path/plugins");
        var result = await loader.LoadAllAsync();

        Assert.Empty(result.Manifests);
    }

    [Fact]
    public async Task DependencyOrdering_LoadsDepBeforeDependent()
    {
        var pluginsDir = CreateTempPluginsDir();
        try
        {
            var depDir = Path.Combine(pluginsDir, "dep-plugin");
            Directory.CreateDirectory(depDir);
            var depManifest = new PluginManifest { Name = "dep-plugin", Version = "1.0.0" };
            await File.WriteAllTextAsync(Path.Combine(depDir, "plugin.json"),
                JsonSerializer.Serialize(depManifest));

            var mainDir = Path.Combine(pluginsDir, "main-plugin");
            Directory.CreateDirectory(mainDir);
            var mainManifest = new PluginManifest
            {
                Name = "main-plugin",
                Version = "1.0.0",
                Dependencies = new Dictionary<string, string> { ["dep-plugin"] = ">=1.0.0" }
            };
            await File.WriteAllTextAsync(Path.Combine(mainDir, "plugin.json"),
                JsonSerializer.Serialize(mainManifest));

            var loader = new PluginLoader(pluginsDir);
            var result = await loader.LoadAllAsync();

            Assert.Equal(2, result.Manifests.Count);
            Assert.Equal("dep-plugin", result.Manifests[0].Name);
            Assert.Equal("main-plugin", result.Manifests[1].Name);
        }
        finally
        {
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task CircularDependency_HandledGracefully()
    {
        var pluginsDir = CreateTempPluginsDir();
        try
        {
            var dirA = Path.Combine(pluginsDir, "plugin-a");
            Directory.CreateDirectory(dirA);
            var manifestA = new PluginManifest
            {
                Name = "plugin-a",
                Version = "1.0.0",
                Dependencies = new Dictionary<string, string> { ["plugin-b"] = ">=1.0.0" }
            };
            await File.WriteAllTextAsync(Path.Combine(dirA, "plugin.json"),
                JsonSerializer.Serialize(manifestA));

            var dirB = Path.Combine(pluginsDir, "plugin-b");
            Directory.CreateDirectory(dirB);
            var manifestB = new PluginManifest
            {
                Name = "plugin-b",
                Version = "1.0.0",
                Dependencies = new Dictionary<string, string> { ["plugin-a"] = ">=1.0.0" }
            };
            await File.WriteAllTextAsync(Path.Combine(dirB, "plugin.json"),
                JsonSerializer.Serialize(manifestB));

            var loader = new PluginLoader(pluginsDir);
            var result = await loader.LoadAllAsync();

            // Should load at least one without throwing
            Assert.True(result.Manifests.Count >= 1);
        }
        finally
        {
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, recursive: true);
        }
    }
}
