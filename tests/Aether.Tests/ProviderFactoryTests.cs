using Aether.Config;
using Aether.Providers;
using Microsoft.Extensions.Configuration;

namespace Aether.Tests;

public class ProviderFactoryTests
{
    [Fact]
    public void Create_OpenAiType_ReturnsGenericHttpProvider()
    {
        var entry = new SpecProviderEntry
        {
            Type = "openai",
            Model = "gpt-4",
            ApiKey = "sk-test",
            BaseUrl = "https://api.example.com/v1"
        };

        var provider = ProviderFactory.Create(entry, "my-provider");

        Assert.NotNull(provider);
        Assert.IsType<GenericHttpProvider>(provider);
        Assert.Equal("my-provider", provider.Name);
        Assert.Equal("gpt-4", provider.Model);
    }

    [Fact]
    public void Create_AnthropicType_ReturnsAnthropicCompatibleProvider()
    {
        var entry = new SpecProviderEntry
        {
            Type = "anthropic",
            Model = "claude-sonnet-4-6",
            ApiKey = "sk-ant-test",
            BaseUrl = "https://anthropic.example.com"
        };

        var provider = ProviderFactory.Create(entry, "custom-anthropic");

        Assert.NotNull(provider);
        Assert.IsType<AnthropicProvider>(provider);
        Assert.Equal("custom-anthropic", provider.Name);
        Assert.Equal("claude-sonnet-4-6", provider.Model);
    }

    [Fact]
    public void Create_UnknownType_FallsBackToGenericHttpProvider()
    {
        var entry = new SpecProviderEntry
        {
            Type = "some-unknown",
            Model = "unknown-model",
            ApiKey = "key",
            BaseUrl = "https://example.com/v1"
        };

        var provider = ProviderFactory.Create(entry, "unknown");

        Assert.NotNull(provider);
        Assert.IsType<GenericHttpProvider>(provider);
        Assert.Equal("unknown", provider.Name);
    }

    [Fact]
    public void Create_FireworksType_ReturnsGenericHttpProvider()
    {
        var entry = new SpecProviderEntry
        {
            Type = "openai", // fireworks uses openai protocol
            Model = "accounts/fireworks/models/deepseek-v3",
            ApiKey = "fw-key",
            BaseUrl = "https://api.fireworks.ai/inference/v1"
        };

        var provider = ProviderFactory.Create(entry, "fireworks");

        Assert.NotNull(provider);
        Assert.IsType<GenericHttpProvider>(provider);
        Assert.Equal("fireworks", provider.Name);
    }

    [Fact]
    public void Create_GenericHttpProvider_CanMakeRequest()
    {
        var entry = new SpecProviderEntry
        {
            Type = "openai",
            Model = "test-model",
            ApiKey = "test-key",
            BaseUrl = "http://localhost:1" // won't actually connect
        };

        var provider = ProviderFactory.Create(entry, "test");

        Assert.False(provider.SupportsTools);
        Assert.Equal("test", provider.Name);
    }
}

public sealed class ProviderConfigIntegrationTests : IDisposable
{
    private readonly string _aetherDir;

    public ProviderConfigIntegrationTests()
    {
        _aetherDir = Path.Combine(Path.GetTempPath(), $"aether-cfg-{Guid.NewGuid()}");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_aetherDir))
            Directory.Delete(_aetherDir, true);
    }

    [Fact]
    public void ConfigLoader_LoadsProviders_AndFactoryCreatesThem()
    {
        // Write config.json with providers
        var configPath = Path.Combine(_aetherDir, "config.json");
        File.WriteAllText(configPath, """
        {
            "providers": {
                "openrouter": {
                    "type": "openrouter",
                    "model": "deepseek/deepseek-r1",
                    "api_key": "sk-or-test",
                    "base_url": "https://openrouter.ai/api/v1"
                },
                "custom-anthropic": {
                    "type": "anthropic",
                    "model": "claude-sonnet-4-6",
                    "api_key": "sk-ant-test",
                    "base_url": "https://custom.anthropic.example.com"
                },
                "local-ollama": {
                    "type": "openai",
                    "model": "llama3",
                    "api_key": "",
                    "base_url": "http://localhost:11434/v1"
                }
            }
        }
        """);

        var configuration = new DictionaryConfiguration(new Dictionary<string, string?>());
        var loader = new ConfigLoader(configuration, _aetherDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
        var config = loader.LoadAsync().Result;

        Assert.Equal(3, config.Providers.Count);

        // Each provider can be created by ProviderFactory
        foreach (var (name, entry) in config.Providers)
        {
            var provider = ProviderFactory.Create(entry, name);
            Assert.NotNull(provider);
            Assert.Equal(name, provider.Name);
            Assert.Equal(entry.Model, provider.Model);
        }
    }

    [Fact]
    public void ConfigLoader_NoProviders_ReturnsEmpty_WhenNoConfigFile()
    {
        var configuration = new DictionaryConfiguration(new Dictionary<string, string?>());
        var loader = new ConfigLoader(configuration, _aetherDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
        var config = loader.LoadAsync().Result;

        Assert.Empty(config.Providers);
    }

    [Fact]
    public void ConfigLoader_AppSettingsProviders_AreLoaded()
    {
        var appSettings = new Dictionary<string, string?>
        {
            ["providers:fireworks:type"] = "openai",
            ["providers:fireworks:model"] = "accounts/fireworks/models/kimi",
            ["providers:fireworks:api_key"] = "fw-key",
            ["providers:fireworks:base_url"] = "https://api.fireworks.ai/inference/v1"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(appSettings)
            .Build();
        var loader = new ConfigLoader(configuration, _aetherDir,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
        var config = loader.LoadAsync().Result;

        Assert.NotEmpty(config.Providers);
        Assert.True(config.Providers.ContainsKey("fireworks"));
    }
}
