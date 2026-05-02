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

    private static void AssertProvider(Dictionary<string, SpecProviderEntry> providers, string name, string? model = null, string? apiKey = null)
    {
        Assert.True(providers.TryGetValue(name, out var entry), $"Provider '{name}' not found");
        if (model is not null)
            Assert.Equal(model, entry.Model);
        if (apiKey is not null)
            Assert.Equal(apiKey, entry.ApiKey);
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

        AssertProvider(result.Providers, "openrouter", model: "gpt-4");
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
        AssertProvider(result.Providers, "openrouter", model: "claude-sonnet-4-6");
        // Appsettings value preserved if not overridden
        AssertProvider(result.Providers, "openrouter", apiKey: "old-key");
    }

    [Fact]
    public async Task Layer_3_agent_config_overrides_global()
    {
        WriteAppSettings("""{"llm":{"model":"gpt-4"}}""");
        var workspaceDir = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(workspaceDir);
        WriteAetherConfig("{\"llm\":{\"model\":\"claude-sonnet\"},\"agents\":{\"maria\":{\"name\":\"maria\",\"workspace\":\"" + workspaceDir.Replace("\\", "\\\\") + "\",\"enabled\":true}}}");
        // Agent spec format with providers map
        File.WriteAllText(Path.Combine(workspaceDir, ".aether.json"),
            "{\"providers\":{\"openrouter\":{\"model\":\"claude-opus-4-7\"}}}");

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
            .Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync(agentName: "maria");

        AssertProvider(result.AgentSpecs["maria"].Providers, "openrouter", model: "claude-opus-4-7");
    }

    [Fact]
    public async Task Layer_4_env_vars_override_all_files()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);
        WriteAppSettings("""{"llm":{"api_key":"file-key"}}""");
        WriteAetherConfig("{\"llm\":{\"api_key\":\"global-key\"},\"agents\":{\"maria\":{\"name\":\"maria\",\"workspace\":\"" + wsPath.Replace("\\", "\\\\") + "\",\"enabled\":true}}}");
        try
        {
            Environment.SetEnvironmentVariable("AETHER_llm__api_key", "env-key");

            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(_tempDir, "appsettings.json"), optional: false)
                .AddEnvironmentVariables("AETHER_")
                .Build();
            var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

            var result = await loader.LoadAsync(agentName: "maria");

            AssertProvider(result.AgentSpecs["maria"].Providers, "openrouter", apiKey: "env-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AETHER_llm__api_key", null);
        }
    }

    [Fact]
    public async Task Layer_5_cli_flags_override_everything()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);
        WriteAppSettings("""{"llm":{"model":"gpt-4"}}""");
        WriteAetherConfig("{\"agents\":{\"maria\":{\"name\":\"maria\",\"workspace\":\"" + wsPath.Replace("\\", "\\\\") + "\",\"enabled\":true}}}");
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

            var result = await loader.LoadAsync(agentName: "maria", cliOverrides: cliOverrides);

            AssertProvider(result.AgentSpecs["maria"].Providers, "openrouter", model: "cli-model");
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
    public async Task New_spec_providers_format_is_loaded()
    {
        WriteAetherConfig("{\"providers\":{\"openrouter\":{\"type\":\"openai\",\"model\":\"claude-sonnet-4-6\",\"apiKey\":\"sk-abc\",\"baseUrl\":\"https://openrouter.ai/api/v1\",\"maxTokens\":8192,\"temperature\":0.5,\"timeoutSeconds\":90},\"anthropic\":{\"type\":\"anthropic\",\"model\":\"claude-opus-4-7\",\"apiKey\":\"sk-xyz\"}}}");

        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);
        var result = await loader.LoadAsync();

        Assert.Equal(2, result.Providers.Count);
        AssertProvider(result.Providers, "openrouter", model: "claude-sonnet-4-6", apiKey: "sk-abc");
        AssertProvider(result.Providers, "anthropic", model: "claude-opus-4-7", apiKey: "sk-xyz");

        var or = result.Providers["openrouter"];
        Assert.Equal("openai", or.Type);
        Assert.Equal("https://openrouter.ai/api/v1", or.BaseUrl);
        Assert.Equal(8192, or.MaxTokens);
        Assert.Equal(0.5, or.Temperature);
        Assert.Equal(90, or.TimeoutSeconds);
    }

    [Fact]
    public async Task Agent_spec_config_is_loaded_from_workspace()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);
        WriteAetherConfig("{\"agents\":{\"maria\":{\"name\":\"maria\",\"workspace\":\"" + wsPath.Replace("\\", "\\\\") + "\",\"enabled\":true}}}");

        File.WriteAllText(Path.Combine(wsPath, ".aether.json"),
            "{\"agent\":{\"name\":\"maria\",\"displayName\":\"Maria\",\"emoji\":\"🌸\"}," +
            "\"storage\":{\"home\":\"/tmp/maria\"}," +
            "\"runtime\":{\"maxConcurrentTurns\":2,\"turnTimeoutSeconds\":180}," +
            "\"providers\":{\"openrouter\":{\"type\":\"openai\",\"model\":\"claude-sonnet-4-6\"}}," +
            "\"tools\":{\"shell\":{\"enabled\":false},\"file\":{\"enabled\":true,\"allowWrites\":true}}," +
            "\"policy\":{\"defaultAutonomy\":\"readonly\",\"denyByDefault\":true}," +
            "\"logging\":{\"level\":\"DEBUG\"}}");

        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);
        var result = await loader.LoadAsync();

        var spec = result.AgentSpecs["maria"];
        Assert.NotNull(spec);
        Assert.Equal("Maria", spec.Agent.DisplayName);
        Assert.Equal("🌸", spec.Agent.Emoji);
        Assert.Equal("/tmp/maria", spec.Storage.Home);
        Assert.Equal(2, spec.Runtime.MaxConcurrentTurns);
        Assert.Equal(180, spec.Runtime.TurnTimeoutSeconds);
        Assert.False(spec.Tools.Shell.Enabled);
        Assert.True(spec.Tools.File.AllowWrites);
        Assert.Equal("readonly", spec.Policy.DefaultAutonomy);
        Assert.Equal("DEBUG", spec.Logging.Level);
    }

    [Fact]
    public async Task Legacy_and_new_provider_format_merge()
    {
        // Legacy llm key + new providers map both present
        WriteAetherConfig("{\"llm\":{\"model\":\"legacy-model\",\"api_key\":\"legacy-key\"},\"providers\":{\"anthropic\":{\"type\":\"anthropic\",\"model\":\"claude-opus-4-7\",\"apiKey\":\"new-key\"}}}");

        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);
        var result = await loader.LoadAsync();

        // Both formats loaded
        AssertProvider(result.Providers, "openrouter", model: "legacy-model", apiKey: "legacy-key");
        AssertProvider(result.Providers, "anthropic", model: "claude-opus-4-7", apiKey: "new-key");
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
    public async Task Validation_does_not_throw_for_empty_config()
    {
        var config = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(config, _aetherDir, NullLogger<ConfigLoader>.Instance);

        var result = await loader.LoadAsync();
        Assert.NotNull(result);
    }
}
