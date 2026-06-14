using System.Text.Json;
using Aether.Agents;
using Aether.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class BackwardCompatibilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public BackwardCompatibilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_bw_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string SerializeConfig(Dictionary<string, object?> config)
    {
        return JsonSerializer.Serialize(config, JsonOpts);
    }

    // ── Legacy gateway.agents.<name>.source ──

    [Fact]
    public async Task Legacy_gateway_agents_source_converted_to_bindings()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);

        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["maria"] = new Dictionary<string, object?>
                {
                    ["name"] = "maria",
                    ["workspace"] = wsPath,
                    ["enabled"] = true
                }
            },
            ["gateway"] = new Dictionary<string, object?>
            {
                ["agents"] = new Dictionary<string, object?>
                {
                    ["maria"] = new Dictionary<string, object?>
                    {
                        ["source"] = "telegram:12345"
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"), SerializeConfig(config));

        var configuration = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(configuration, _aetherDir, NullLogger<ConfigLoader>.Instance);
        await loader.LoadAsync();

        var agent = loader.GetAgentConfig("maria");
        Assert.NotNull(agent);
        Assert.Contains("telegram:12345", agent!.Bindings);
    }

    [Fact]
    public async Task Legacy_gateway_agents_source_does_not_duplicate_existing_bindings()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);

        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["maria"] = new Dictionary<string, object?>
                {
                    ["name"] = "maria",
                    ["workspace"] = wsPath,
                    ["enabled"] = true,
                    ["bindings"] = new[] { "telegram:12345" }
                }
            },
            ["gateway"] = new Dictionary<string, object?>
            {
                ["agents"] = new Dictionary<string, object?>
                {
                    ["maria"] = new Dictionary<string, object?>
                    {
                        ["source"] = "telegram:12345"
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"), SerializeConfig(config));

        var configuration = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(configuration, _aetherDir, NullLogger<ConfigLoader>.Instance);
        await loader.LoadAsync();

        var agent = loader.GetAgentConfig("maria");
        Assert.NotNull(agent);
        Assert.Single(agent!.Bindings);
    }

    [Fact]
    public async Task Legacy_gateway_ignored_when_agent_not_in_agents_section()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);

        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["default"] = new Dictionary<string, object?>
                {
                    ["name"] = "default",
                    ["workspace"] = wsPath,
                    ["enabled"] = true
                }
            },
            ["gateway"] = new Dictionary<string, object?>
            {
                ["agents"] = new Dictionary<string, object?>
                {
                    ["unknown-agent"] = new Dictionary<string, object?>
                    {
                        ["source"] = "telegram:99999"
                    }
                }
            }
        };
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"), SerializeConfig(config));

        var configuration = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(configuration, _aetherDir, NullLogger<ConfigLoader>.Instance);
        await loader.LoadAsync();
    }

    // ── Legacy provider format ──

    [Fact]
    public async Task Legacy_llm_provider_format_still_works()
    {
        var configJson = """{"llm":{"api_key":"sk-legacy","model":"legacy-model","timeout_seconds":90}}""";
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"), configJson);

        var configuration = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(configuration, _aetherDir, NullLogger<ConfigLoader>.Instance);
        var result = await loader.LoadAsync();

        Assert.True(result.Providers.ContainsKey("openrouter"));
        Assert.Equal("legacy-model", result.Providers["openrouter"].Model);
        Assert.Equal("sk-legacy", result.Providers["openrouter"].ApiKey);
    }

    // ── Provider-specific env vars ──

    [Fact]
    public async Task Provider_specific_env_vars_override_config()
    {
        var wsPath = Path.Combine(_aetherDir, "workspaces", "maria");
        Directory.CreateDirectory(wsPath);

        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["maria"] = new Dictionary<string, object?>
                {
                    ["name"] = "maria",
                    ["workspace"] = wsPath,
                    ["enabled"] = true
                }
            },
            ["providers"] = new Dictionary<string, object?>
            {
                ["fireworks"] = new Dictionary<string, object?>
                {
                    ["type"] = "openai",
                    ["model"] = "config-model",
                    ["apiKey"] = "config-key"
                }
            }
        };
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"), SerializeConfig(config));

        try
        {
            Environment.SetEnvironmentVariable("AETHER_PROVIDERS_FIREWORKS_API_KEY", "env-fireworks-key");

            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables("AETHER_")
                .Build();
            var loader = new ConfigLoader(configuration, _aetherDir, NullLogger<ConfigLoader>.Instance);
            var result = await loader.LoadAsync(agentName: "maria");

            Assert.True(result.AgentSpecs["maria"].Providers.ContainsKey("fireworks"));
            Assert.Equal("env-fireworks-key", result.AgentSpecs["maria"].Providers["fireworks"].ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AETHER_PROVIDERS_FIREWORKS_API_KEY", null);
        }
    }

    // ── repo-relative paths ──

    [Fact]
    public async Task AgentProfile_resolves_from_current_dir_when_no_aether_home()
    {
        var legacyDir = Path.Combine(_tempDir, "agents", "testagent");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "SOUL.md"), "I am a legacy agent.");

        var originalCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _tempDir;
            var resolvedTempDir = Environment.CurrentDirectory;
            var resolvedLegacyDir = Path.Combine(resolvedTempDir, "agents", "testagent");

            var aetherDir = Path.Combine(resolvedTempDir, ".aether");
            Directory.CreateDirectory(aetherDir);
            File.WriteAllText(Path.Combine(aetherDir, "config.json"), "{}");
            var configuration = new ConfigurationBuilder().Build();
            var loader = new ConfigLoader(configuration, aetherDir, NullLogger<ConfigLoader>.Instance);
            await loader.LoadAsync();

            var profile = AgentProfile.FromConfigLoader(
                "testagent",
                loader,
                new AgentConfig { StartupFiles = new() { "SOUL.md" } });

            Assert.Equal(resolvedLegacyDir, profile.AgentDirectory);
            var persona = await profile.LoadPersonaAsync();
            Assert.Contains("I am a legacy agent.", persona);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }
}
