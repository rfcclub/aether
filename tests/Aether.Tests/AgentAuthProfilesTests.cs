using Aether.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class AgentAuthProfilesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;

    public AgentAuthProfilesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_auth_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateAuthDirectory_creates_three_json_files()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);

        await profiles.CreateAuthDirectoryAsync("testagent");

        var agentDir = Path.Combine(_aetherDir, "agents", "testagent", "agent");
        Assert.True(File.Exists(Path.Combine(agentDir, "auth-state.json")));
        Assert.True(File.Exists(Path.Combine(agentDir, "auth-profiles.json")));
        Assert.True(File.Exists(Path.Combine(agentDir, "models.json")));
    }

    [Fact]
    public async Task AuthState_default_content()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        await profiles.CreateAuthDirectoryAsync("testagent");

        var config = await profiles.LoadAuthProfilesAsync("testagent");
        Assert.Null(config.State.ActiveProvider);
        Assert.Null(config.State.ActiveModel);
    }

    [Fact]
    public async Task AuthProfiles_default_content()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        await profiles.CreateAuthDirectoryAsync("testagent");

        var config = await profiles.LoadAuthProfilesAsync("testagent");
        Assert.Empty(config.Profiles);
    }

    [Fact]
    public async Task Models_default_content()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        await profiles.CreateAuthDirectoryAsync("testagent");

        var config = await profiles.LoadAuthProfilesAsync("testagent");
        Assert.Null(config.Model.Primary);
        Assert.Empty(config.Model.Fallbacks);
    }

    [Fact]
    public async Task Load_returns_empty_config_when_agent_has_no_auth_dir()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);

        var config = await profiles.LoadAuthProfilesAsync("nonexistent");

        Assert.Null(config.State.ActiveProvider);
        Assert.Empty(config.Profiles);
        Assert.Null(config.Model.Primary);
    }

    [Fact]
    public async Task Save_and_load_auth_profiles_round_trip()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        await profiles.CreateAuthDirectoryAsync("testagent");

        var config = new AgentAuthConfig
        {
            State = new AuthState { ActiveProvider = "openrouter", ActiveModel = "claude-opus-4-7" },
            Profiles = new Dictionary<string, AuthProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["openrouter"] = new() { Mode = "api_key", ApiKey = "test-key-123", Email = "test@example.com" }
            },
            Model = new AgentModelConfig { Primary = "openrouter/claude", Fallbacks = { "anthropic/claude" } }
        };
        await profiles.SaveAuthProfilesAsync("testagent", config);

        var loaded = await profiles.LoadAuthProfilesAsync("testagent");
        Assert.Equal("openrouter", loaded.State.ActiveProvider);
        Assert.Equal("claude-opus-4-7", loaded.State.ActiveModel);
        Assert.True(loaded.Profiles.ContainsKey("openrouter"));
        Assert.Equal("api_key", loaded.Profiles["openrouter"].Mode);
        Assert.Equal("test-key-123", loaded.Profiles["openrouter"].ApiKey);
        Assert.Equal("test@example.com", loaded.Profiles["openrouter"].Email);
    }

    [Fact]
    public async Task CreateAuthDirectory_is_idempotent()
    {
        var profiles = new AgentAuthProfiles(_aetherDir, NullLogger<AgentAuthProfiles>.Instance);
        await profiles.CreateAuthDirectoryAsync("testagent");
        var config = new AgentAuthConfig
        {
            State = new AuthState { ActiveProvider = "anthropic" }
        };
        await profiles.SaveAuthProfilesAsync("testagent", config);

        // Second create should not overwrite
        await profiles.CreateAuthDirectoryAsync("testagent");

        var loaded = await profiles.LoadAuthProfilesAsync("testagent");
        Assert.Equal("anthropic", loaded.State.ActiveProvider);
    }
}
