using Aether.Agents;
using Aether.Agents;
using Aether.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class AgentProfileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agentDir;

    public AgentProfileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether-test-profile-{Guid.NewGuid()}");
        _agentDir = Path.Combine(_tempDir, "agent");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadPersonaAsync_LoadsConfiguredFilesInOrder()
    {
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "I am Maria.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");

        var config = new AgentConfig { StartupFiles = new() { "SOUL.md", "USER.md" } };
        var profile = new AgentProfile("maria", _agentDir, config, new AgentModelConfig());

        var persona = await profile.LoadPersonaAsync();

        Assert.Contains("I am Maria.", persona);
        Assert.Contains("User is Thoor.", persona);
        var soulIndex = persona.IndexOf("I am Maria.");
        var userIndex = persona.IndexOf("User is Thoor.");
        Assert.True(soulIndex < userIndex, "SOUL.md should load before USER.md");
    }

    [Fact]
    public async Task LoadPersonaAsync_SkipsMissingOptionalFiles()
    {
        var config = new AgentConfig { StartupFiles = new() { "SOUL.md", "NONEXISTENT.md" } };
        var profile = new AgentProfile("maria", _agentDir, config, new AgentModelConfig());

        var persona = await profile.LoadPersonaAsync();

        Assert.NotNull(persona);
    }

    [Fact]
    public async Task LoadDailyMemoryAsync_LoadsTodayAndYesterday()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(_agentDir, "memory", $"{today}.md"), "Today's notes.");
        File.WriteAllText(Path.Combine(_agentDir, "memory", $"{yesterday}.md"), "Yesterday's notes.");

        var profile = new AgentProfile("maria", _agentDir, new AgentConfig(), new AgentModelConfig());

        var memory = await profile.LoadDailyMemoryAsync();

        Assert.Contains("Today's notes.", memory);
        Assert.Contains("Yesterday's notes.", memory);
    }

    [Fact]
    public async Task LoadFileAsync_ReturnsNullForMissingFile()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig(), new AgentModelConfig());

        var result = await profile.LoadFileAsync("NONEXISTENT.md");

        Assert.Null(result);
    }

    [Fact]
    public void Name_ReturnsConfiguredName()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig(), new AgentModelConfig());

        Assert.Equal("maria", profile.Name);
    }

    // ── FromConfigLoader tests ──

    private static string SerializeConfig(Dictionary<string, object?> config)
    {
        return System.Text.Json.JsonSerializer.Serialize(config,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    }

    [Fact]
    public async Task FromConfigLoader_resolves_workspace_from_config()
    {
        var aetherDir = Path.Combine(_tempDir, ".aether");
        var workspaceDir = Path.Combine(aetherDir, "workspaces", "testagent");
        Directory.CreateDirectory(workspaceDir);
        File.WriteAllText(Path.Combine(workspaceDir, "SOUL.md"), "config-workspace soul");

        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["testagent"] = new Dictionary<string, object?>
                {
                    ["name"] = "testagent",
                    ["workspace"] = workspaceDir,
                    ["enabled"] = true
                }
            }
        };
        Directory.CreateDirectory(aetherDir);
        File.WriteAllText(Path.Combine(aetherDir, "config.json"), SerializeConfig(config));

        var configuration = new ConfigurationBuilder().Build();
        var configLoader = new ConfigLoader(configuration, aetherDir, NullLogger<ConfigLoader>.Instance);
        await configLoader.LoadAsync();

        var profile = AgentProfile.FromConfigLoader("testagent", configLoader, new AgentConfig());

        Assert.Equal(workspaceDir, profile.AgentDirectory);
    }

    [Fact]
    public async Task FromConfigLoader_falls_back_to_legacy_path()
    {
        var aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(aetherDir);
        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["testagent"] = new Dictionary<string, object?>
                {
                    ["name"] = "testagent",
                    ["workspace"] = "/nonexistent/workspace",
                    ["enabled"] = true
                }
            }
        };
        File.WriteAllText(Path.Combine(aetherDir, "config.json"), SerializeConfig(config));

        var legacyDir = Path.Combine(Environment.CurrentDirectory, "agents", "testagent");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "SOUL.md"), "legacy soul");
        try
        {
            var configuration = new ConfigurationBuilder().Build();
            var configLoader = new ConfigLoader(configuration, aetherDir, NullLogger<ConfigLoader>.Instance);
            await configLoader.LoadAsync();

            var profile = AgentProfile.FromConfigLoader("testagent", configLoader, new AgentConfig());

            Assert.Equal(legacyDir, profile.AgentDirectory);
        }
        finally
        {
            if (Directory.Exists(legacyDir))
                Directory.Delete(legacyDir, recursive: true);
        }
    }

    [Fact]
    public async Task FromConfigLoader_throws_when_neither_path_exists()
    {
        var aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(aetherDir);
        var config = new Dictionary<string, object?>
        {
            ["agents"] = new Dictionary<string, object?>
            {
                ["testagent"] = new Dictionary<string, object?>
                {
                    ["name"] = "testagent",
                    ["workspace"] = "/nonexistent/workspace",
                    ["enabled"] = true
                }
            }
        };
        File.WriteAllText(Path.Combine(aetherDir, "config.json"), SerializeConfig(config));

        var configuration = new ConfigurationBuilder().Build();
        var configLoader = new ConfigLoader(configuration, aetherDir, NullLogger<ConfigLoader>.Instance);
        await configLoader.LoadAsync();

        Assert.Throws<DirectoryNotFoundException>(() =>
            AgentProfile.FromConfigLoader("testagent", configLoader, new AgentConfig()));
    }
}
