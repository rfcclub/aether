using System.Text.Json;
using Aether.Config;
using Aether.Providers;

namespace Aether.Tests;

/// <summary>
/// Tests for ProviderRegistrar — converts a parsed ProviderTemplate + resolved key
/// into a SpecProviderEntry and writes it into config.json. Shared by CLI + wizard.
/// </summary>
public sealed class ProviderRegistrarTests : IDisposable
{
    private readonly string _aetherDir;

    public ProviderRegistrarTests()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"aether_reg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        _aetherDir = Path.Combine(temp, ".aether");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_aetherDir))
            Directory.Delete(_aetherDir, recursive: true);
    }

    // --- ToSpecProviderEntry ---

    [Fact]
    public void ToSpecProviderEntry_maps_openai_template()
    {
        var template = new ProviderTemplate
        {
            Id = "nahcrof",
            Label = "Nahcrof AI",
            Api = "openai-completions",
            MappedType = "openai",
            BaseUrl = "https://crof.ai/v1",
            ApiKeyRef = "${NAHCROF_API_KEY}",
            Models = new[] { "glm-4.7-flash", "glm-5.2", "deepseek-v4-pro" }
        };

        var entry = ProviderRegistrar.ToSpecProviderEntry(template, "resolved-key-123");

        Assert.Equal("openai", entry.Type);
        Assert.Equal("https://crof.ai/v1", entry.BaseUrl);
        Assert.Equal("resolved-key-123", entry.ApiKey);
        Assert.Equal("glm-4.7-flash", entry.Model); // first model
        Assert.Equal(new[] { "glm-4.7-flash", "glm-5.2", "deepseek-v4-pro" }, entry.Models);
    }

    [Fact]
    public void ToSpecProviderEntry_maps_anthropic_template()
    {
        var template = new ProviderTemplate
        {
            Id = "minimax",
            Label = "MiniMax",
            Api = "anthropic-messages",
            MappedType = "anthropic",
            BaseUrl = "https://api.minimax.io/anthropic",
            ApiKeyRef = "${MINIMAX_API_KEY}",
            Models = new[] { "MiniMax-M2.7", "MiniMax-M2.7-highspeed" }
        };

        var entry = ProviderRegistrar.ToSpecProviderEntry(template, "mm-key");

        Assert.Equal("anthropic", entry.Type);
        Assert.Equal("MiniMax-M2.7", entry.Model);
        Assert.Equal(2, entry.Models!.Count);
    }

    [Fact]
    public void ToSpecProviderEntry_throws_for_unsupported_adapter()
    {
        var template = new ProviderTemplate
        {
            Id = "gemini",
            Label = "Gemini",
            Api = "generic",
            MappedType = null, // unsupported
            BaseUrl = "https://generativelanguage.googleapis.com",
            ApiKeyRef = "AIzaSyLiteral",
            Models = new[] { "gemini-3.1-flash-lite-preview" }
        };

        Assert.Throws<InvalidOperationException>(() =>
            ProviderRegistrar.ToSpecProviderEntry(template, "key"));
    }

    [Fact]
    public void ToSpecProviderEntry_uses_first_model_when_models_present()
    {
        var template = new ProviderTemplate
        {
            Id = "r9",
            Api = "openai-completions",
            MappedType = "openai",
            BaseUrl = "http://127.0.0.1:20128/v1",
            ApiKeyRef = "${NINE_ROUTER_KEY:-none}",
            Models = new[] { "aether", "nw/glm-5.2" }
        };

        var entry = ProviderRegistrar.ToSpecProviderEntry(template, "k");
        Assert.Equal("aether", entry.Model);
    }

    // --- WriteProviderAsync ---

    [Fact]
    public async Task WriteProviderAsync_creates_config_when_missing()
    {
        var template = MakeOpenAiTemplate();
        var configPath = Path.Combine(_aetherDir, "config.json");
        Assert.False(File.Exists(configPath));

        await ProviderRegistrar.WriteProviderAsync(_aetherDir, "nahcrof", template, "resolved-key", default);

        Assert.True(File.Exists(configPath));
        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("providers", out var providers));
        Assert.True(providers.TryGetProperty("nahcrof", out var entry));
        Assert.Equal("openai", entry.GetProperty("type").GetString());
        Assert.Equal("resolved-key", entry.GetProperty("api_key").GetString());
        Assert.Equal("https://crof.ai/v1", entry.GetProperty("base_url").GetString());
    }

    [Fact]
    public async Task WriteProviderAsync_overwrites_existing_provider_entry()
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "providers": {
                "nahcrof": { "type": "openai", "api_key": "OLD", "model": "old-model" }
              }
            }
            """);

        var template = MakeOpenAiTemplate();
        await ProviderRegistrar.WriteProviderAsync(_aetherDir, "nahcrof", template, "NEW-key", default);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.GetProperty("providers").GetProperty("nahcrof");
        Assert.Equal("NEW-key", entry.GetProperty("api_key").GetString());
        Assert.Equal("glm-4.7-flash", entry.GetProperty("model").GetString());
    }

    [Fact]
    public async Task WriteProviderAsync_preserves_other_providers_and_agents()
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "providers": {
                "openrouter": { "type": "openai", "api_key": "or-key", "model": "m-or" }
              },
              "agents": {
                "default": { "name": "default", "enabled": true }
              },
              "meta": { "version": "3.0.1" }
            }
            """);

        var template = MakeOpenAiTemplate();
        await ProviderRegistrar.WriteProviderAsync(_aetherDir, "nahcrof", template, "nahcrof-key", default);

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);

        // new provider added
        Assert.True(doc.RootElement.GetProperty("providers").TryGetProperty("nahcrof", out _));
        // existing provider preserved
        Assert.True(doc.RootElement.GetProperty("providers").TryGetProperty("openrouter", out var or));
        Assert.Equal("or-key", or.GetProperty("api_key").GetString());
        // agents + meta preserved
        Assert.True(doc.RootElement.GetProperty("agents").TryGetProperty("default", out _));
        Assert.Equal("3.0.1", doc.RootElement.GetProperty("meta").GetProperty("version").GetString());
    }

    [Fact]
    public async Task WriteProviderAsync_throws_for_unsupported_adapter()
    {
        var template = new ProviderTemplate
        {
            Id = "gemini",
            Api = "generic",
            MappedType = null,
            BaseUrl = "https://x.example",
            ApiKeyRef = "lit",
            Models = new[] { "m" }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ProviderRegistrar.WriteProviderAsync(_aetherDir, "gemini", template, "k", default));
    }

    private static ProviderTemplate MakeOpenAiTemplate() => new()
    {
        Id = "nahcrof",
        Label = "Nahcrof AI",
        Api = "openai-completions",
        MappedType = "openai",
        BaseUrl = "https://crof.ai/v1",
        ApiKeyRef = "${NAHCROF_API_KEY}",
        Models = new[] { "glm-4.7-flash", "glm-5.2" }
    };
}

