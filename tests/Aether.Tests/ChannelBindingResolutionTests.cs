using System.Text.Json;
using Aether.Channels;
using Aether.Config;
using Aether.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class ChannelBindingResolutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;

    public ChannelBindingResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_bind_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task WriteConfigWithAgents(Dictionary<string, object> agents)
    {
        var config = new Dictionary<string, object?> { ["agents"] = agents };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(Path.Combine(_aetherDir, "config.json"), json);
    }

    private async Task<ConfigLoader> CreateConfigLoader()
    {
        var configuration = new ConfigurationBuilder().Build();
        var loader = new ConfigLoader(configuration, _aetherDir, NullLogger<ConfigLoader>.Instance);
        await loader.LoadAsync();
        return loader;
    }

    [Fact]
    public async Task Router_matches_binding_to_agent()
    {
        await WriteConfigWithAgents(new()
        {
            ["maria"] = new Dictionary<string, object?>
            {
                ["name"] = "maria",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "maria"),
                ["enabled"] = true,
                ["bindings"] = new[] { "telegram:12345" }
            }
        });

        var configLoader = await CreateConfigLoader();

        // Create agent config in configLoader cache
        var agentEntry = configLoader.GetAgentConfig("maria");
        Assert.NotNull(agentEntry);

        var router = new MessageRouter(configLoader, NullLogger<MessageRouter>.Instance);

        var message = new InboundMessage("msg-1", "telegram", "12345", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(message);

        Assert.NotNull(result);
        Assert.Equal("maria", result!.Value.AgentName);
    }

    [Fact]
    public async Task Router_returns_null_for_no_match()
    {
        await WriteConfigWithAgents(new()
        {
            ["maria"] = new Dictionary<string, object?>
            {
                ["name"] = "maria",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "maria"),
                ["enabled"] = true,
                ["bindings"] = new[] { "telegram:99999" }
            }
        });

        var configLoader = await CreateConfigLoader();
        var router = new MessageRouter(configLoader, NullLogger<MessageRouter>.Instance);

        var message = new InboundMessage("msg-1", "telegram", "12345", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(message);

        Assert.Null(result);
    }

    [Fact]
    public async Task Router_fallback_to_default_agent()
    {
        await WriteConfigWithAgents(new()
        {
            ["default"] = new Dictionary<string, object?>
            {
                ["name"] = "default",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "default"),
                ["enabled"] = true,
                ["bindings"] = new string[] { }
            }
        });

        var configLoader = await CreateConfigLoader();
        var router = new MessageRouter(configLoader, NullLogger<MessageRouter>.Instance);

        var message = new InboundMessage("msg-1", "telegram", "unknown-chat", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(message);

        Assert.NotNull(result);
        Assert.Equal("default", result!.Value.AgentName);
    }

    [Fact]
    public async Task Router_fallback_to_first_enabled_agent()
    {
        await WriteConfigWithAgents(new()
        {
            ["alpha"] = new Dictionary<string, object?>
            {
                ["name"] = "alpha",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "alpha"),
                ["enabled"] = true,
                ["bindings"] = new string[] { }
            },
            ["beta"] = new Dictionary<string, object?>
            {
                ["name"] = "beta",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "beta"),
                ["enabled"] = true,
                ["bindings"] = new string[] { }
            }
        });

        var configLoader = await CreateConfigLoader();
        var router = new MessageRouter(configLoader, NullLogger<MessageRouter>.Instance);

        var message = new InboundMessage("msg-1", "telegram", "unknown", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(message);

        Assert.NotNull(result);
        Assert.Equal("alpha", result!.Value.AgentName); // first enabled
    }

    [Fact]
    public async Task Router_skips_disabled_agents()
    {
        await WriteConfigWithAgents(new()
        {
            ["alpha"] = new Dictionary<string, object?>
            {
                ["name"] = "alpha",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "alpha"),
                ["enabled"] = false,
                ["bindings"] = new[] { "telegram:12345" }
            },
            ["beta"] = new Dictionary<string, object?>
            {
                ["name"] = "beta",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "beta"),
                ["enabled"] = true,
                ["bindings"] = new[] { "telegram:12345" }
            }
        });

        var configLoader = await CreateConfigLoader();
        var router = new MessageRouter(configLoader, NullLogger<MessageRouter>.Instance);

        var message = new InboundMessage("msg-1", "telegram", "12345", "user1", "hello", DateTimeOffset.UtcNow);
        var result = await router.RouteAsync(message);

        Assert.NotNull(result);
        Assert.Equal("beta", result!.Value.AgentName);
    }

    [Fact]
    public async Task Router_skips_bot_messages()
    {
        await WriteConfigWithAgents(new()
        {
            ["maria"] = new Dictionary<string, object?>
            {
                ["name"] = "maria",
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", "maria"),
                ["enabled"] = true,
                ["bindings"] = new[] { "telegram:12345" }
            }
        });

        var configLoader = await CreateConfigLoader();
        var router = new MessageRouter(configLoader, NullLogger<MessageRouter>.Instance);

        var message = new InboundMessage("msg-1", "telegram", "12345", "bot-user", "hello",
            DateTimeOffset.UtcNow, IsFromBot: true);
        var result = await router.RouteAsync(message);

        Assert.Null(result);
    }
}
