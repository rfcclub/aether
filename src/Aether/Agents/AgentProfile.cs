using Aether.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Agents;

public sealed class AgentProfile : IAgentProfile
{
    private readonly AgentConfig _config;

    public string Name { get; }
    public string AgentDirectory { get; }

    public AgentProfile(string name, string agentDirectory, AgentConfig config)
    {
        Name = name;
        AgentDirectory = agentDirectory;
        _config = config;
    }

    public static AgentProfile FromConfigLoader(
        string name,
        ConfigLoader configLoader,
        AgentConfig config,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        // Try ~/.aether/workspaces/<name>/ first
        var agentConfig = configLoader.GetAgentConfig(name);
        var newPath = agentConfig?.Workspace;
        if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
        {
            return new AgentProfile(name, newPath, config);
        }

        // Fallback: <cwd>/agents/<name>/ (legacy layout)
        var legacyPath = Path.Combine(Environment.CurrentDirectory, "agents", name);
        if (Directory.Exists(legacyPath))
        {
            logger.LogWarning("Agent '{Name}' using legacy path {Path}. Migrate to ~/.aether/workspaces/{Name}/",
                name, legacyPath, name);
            return new AgentProfile(name, legacyPath, config);
        }

        throw new DirectoryNotFoundException(
            $"Agent directory not found for '{name}'. " +
            $"Tried: {newPath ?? "<no workspace in config>"} and {legacyPath}");
    }

    public async Task<string> LoadPersonaAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        foreach (var file in _config.StartupFiles)
        {
            var content = await LoadFileAsync(file, ct);
            if (content is not null)
                parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }

    public async Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(AgentDirectory, relativePath);
        if (!File.Exists(fullPath))
            return null;
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task<string> LoadDailyMemoryAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        var dates = new[] { DateTime.UtcNow, DateTime.UtcNow.AddDays(-1) };
        foreach (var date in dates)
        {
            var filename = $"{date:yyyy-MM-dd}.md";
            var content = await LoadFileAsync(Path.Combine(_config.DailyMemoryDirectory, filename), ct);
            if (content is not null)
                parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }
}
