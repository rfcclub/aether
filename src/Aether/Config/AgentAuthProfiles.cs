using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Aether.Config;

public sealed record AgentAuthConfig
{
    public AuthState State { get; init; } = new();
    public Dictionary<string, AuthProfile> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public AgentModelConfig Model { get; init; } = new();
}

public sealed record AuthState
{
    public string? ActiveProvider { get; init; }
    public string? ActiveModel { get; init; }
}

public sealed record AuthProfile
{
    public string Mode { get; init; } = "";
    public string? ApiKey { get; init; }
    public string? Email { get; init; }
}

public sealed class AgentAuthProfiles
{
    private readonly string _aetherDir;
    private readonly ILogger<AgentAuthProfiles> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentAuthProfiles(string aetherDir, ILogger<AgentAuthProfiles> logger)
    {
        _aetherDir = aetherDir;
        _logger = logger;
    }

    public async Task CreateAuthDirectoryAsync(string agentName, CancellationToken ct = default)
    {
        var agentDir = Path.Combine(_aetherDir, "agents", agentName, "agent");
        Directory.CreateDirectory(agentDir);

        var authStatePath = Path.Combine(agentDir, "auth-state.json");
        if (!File.Exists(authStatePath))
            await WriteJsonAsync(authStatePath, new { activeProvider = (string?)null, activeModel = (string?)null }, ct);

        var authProfilesPath = Path.Combine(agentDir, "auth-profiles.json");
        if (!File.Exists(authProfilesPath))
            await WriteJsonAsync(authProfilesPath, new { }, ct);

        var modelsPath = Path.Combine(agentDir, "models.json");
        if (!File.Exists(modelsPath))
            await WriteJsonAsync(modelsPath, new { primary = (string?)null, fallbacks = Array.Empty<string>() }, ct);

        SetPermissions(agentDir);
    }

    public async Task<AgentAuthConfig> LoadAuthProfilesAsync(string agentName, CancellationToken ct = default)
    {
        var agentDir = GetAgentDir(agentName);
        var config = new AgentAuthConfig();

        config = config with { State = await LoadJsonAsync<AuthState>(agentDir, "auth-state.json", ct) ?? new AuthState() };
        config = config with { Model = await LoadJsonAsync<AgentModelConfig>(agentDir, "models.json", ct) ?? new AgentModelConfig() };

        var profiles = await LoadJsonAsync<Dictionary<string, AuthProfile>>(agentDir, "auth-profiles.json", ct);
        config = config with { Profiles = profiles ?? new(StringComparer.OrdinalIgnoreCase) };

        return config;
    }

    public async Task SaveAuthProfilesAsync(string agentName, AgentAuthConfig config, CancellationToken ct = default)
    {
        var agentDir = GetAgentDir(agentName);

        await WriteJsonAsync(
            Path.Combine(agentDir, "auth-state.json"),
            new { activeProvider = config.State.ActiveProvider, activeModel = config.State.ActiveModel },
            ct);

        await WriteJsonAsync(
            Path.Combine(agentDir, "auth-profiles.json"),
            config.Profiles,
            ct);

        await WriteJsonAsync(
            Path.Combine(agentDir, "models.json"),
            new { primary = config.Model.Primary, fallbacks = config.Model.Fallbacks },
            ct);
    }

    private string GetAgentDir(string agentName) =>
        Path.Combine(_aetherDir, "agents", agentName, "agent");

    private static async Task<T?> LoadJsonAsync<T>(string dir, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return default;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static void SetPermissions(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                System.Diagnostics.Process.Start("chmod", $"700 \"{path}\"")?.WaitForExit(1000);
            }
            catch
            {
                // Best effort — non-fatal if chmod fails
            }
        }
    }
}
