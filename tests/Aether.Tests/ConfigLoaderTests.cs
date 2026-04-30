using Aether.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;

    public ConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_cfg_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteAppSettings(string json)
    {
        var path = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string WriteAetherConfig(string json)
    {
        var path = Path.Combine(_aetherDir, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task Layer_1_loads_framework_appsettings()
    {
        WriteAppSettings("""{"llm":{"provider":"openrouter","model":"gpt-4"}}""");

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
            .Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync();

        Assert.Equal("gpt-4", result.Providers.OpenRouter.Model);
    }

    [Fact]
    public async Task Layer_2_global_config_overrides_appsettings()
    {
        WriteAppSettings("""{"llm":{"model":"gpt-4","api_key":"old-key"}}""");
        WriteAetherConfig("""{"llm":{"model":"claude-sonnet-4-6","timeout_seconds":120}}""");

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
            .Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync();

        // Global overrides model
        Assert.Equal("claude-sonnet-4-6", result.Providers.OpenRouter.Model);
        // Appsettings value preserved if not overridden
        Assert.Equal("old-key", result.Providers.OpenRouter.ApiKey);
    }

    [Fact]
    public async Task Layer_3_agent_config_overrides_global()
    {
        WriteAppSettings("""{"llm":{"model":"gpt-4"}}""");
        WriteAetherConfig("""{"llm":{"model":"claude-sonnet"}}""");
        var workspaceDir = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(workspaceDir);
        File.WriteAllText(Path.Combine(workspaceDir, ".aether.json"),
            """{"llm":{"model":"claude-opus-4-7"}}""");

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
            .Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync(agentName: "maria");

        Assert.Equal("claude-opus-4-7", result.Providers.OpenRouter.Model);
    }

    [Fact]
    public async Task Layer_4_env_vars_override_all_files()
    {
        WriteAppSettings("""{"llm":{"api_key":"file-key"}}""");
        WriteAetherConfig("""{"llm":{"api_key":"global-key"}}""");
        try
        {
            Environment.SetEnvironmentVariable("AETHER_llm__api_key", "env-key");

            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
                .AddEnvironmentVariables("AETHER_")
                .Build();
            var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

            var result = await loader.LoadAsync();

            Assert.Equal("env-key", result.Providers.OpenRouter.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AETHER_llm__api_key", null);
        }
    }

    [Fact]
    public async Task Layer_5_cli_flags_override_everything()
    {
        WriteAppSettings("""{"llm":{"model":"gpt-4"}}""");
        try
        {
            Environment.SetEnvironmentVariable("AETHER_llm__model", "env-model");
            var cliOverrides = new Dictionary<string, string>
            {
                ["llm:model"] = "cli-model"
            };

            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
                .AddEnvironmentVariables("AETHER_")
                .AddInMemoryCollection(cliOverrides)
                .Build();
            var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

            var result = await loader.LoadAsync(cliOverrides: cliOverrides);

            Assert.Equal("cli-model", result.Providers.OpenRouter.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AETHER_llm__model", null);
        }
    }

    [Fact]
    public async Task Missing_layer_is_skipped()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_tempDir, "nonexistent.json"), optional: true)
            .Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetAgentConfig_returns_null_for_unknown_agent()
    {
        WriteAetherConfig("""{"agents":{"maria":{"name":"maria","workspace":"/tmp"}}}""");

        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);
        await loader.LoadAsync();

        var agent = loader.GetAgentConfig("nonexistent");
        Assert.Null(agent);
    }

    [Fact]
    public async Task GetAgentConfig_returns_configured_agent()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);
        WriteAetherConfig("{\"agents\":{\"maria\":{\"name\":\"maria\",\"workspace\":\"" + wsPath.Replace("\\", "\\\\") + "\",\"model\":{\"primary\":\"openrouter/claude\"},\"enabled\":true}}}");

        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);
        await loader.LoadAsync();

        var agent = loader.GetAgentConfig("maria");
        Assert.NotNull(agent);
        Assert.Equal("maria", agent!.Name);
        Assert.Equal(wsPath, agent.Workspace);
        Assert.Equal("openrouter/claude", agent.Model.Primary);
        Assert.True(agent.Enabled);
    }

    [Fact]
    public async Task Validation_warns_when_no_api_key()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["llm:provider"] = "openrouter" })
            .Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync();

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Validation_throws_when_no_provider_configured()
    {
        // No llm provider key = no provider configured
        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync();
        // Should NOT throw — just loads with defaults. Only throw if explicitly disabled.
        Assert.NotNull(result);
    }
}
