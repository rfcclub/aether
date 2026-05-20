using Aether.Agent;
using Aether.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Agents;

public sealed class AgentProfile
{
    private readonly AgentConfig _config;
    private ContextAssembler? _contextAssembler;

    public string Name { get; }
    public string AgentDirectory { get; }
    public AgentModelConfig Model { get; }

    public AgentProfile(string name, string agentDirectory, AgentConfig config, AgentModelConfig model)
    {
        Name = name;
        AgentDirectory = agentDirectory;
        _config = config;
        Model = model;
    }

    public static AgentProfile FromConfigLoader(
        string name,
        ConfigLoader configLoader,
        AgentConfig config,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var agentConfig = configLoader.GetAgentConfig(name);
        var newPath = agentConfig?.Workspace;
        var model = agentConfig?.Model ?? new AgentModelConfig();

        if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
        {
            return new AgentProfile(name, newPath, config, model);
        }

        // Fallback: <cwd>/agents/<name>/ (legacy layout)
        var legacyPath = Path.Combine(Environment.CurrentDirectory, "agents", name);
        if (Directory.Exists(legacyPath))
        {
            logger.LogWarning("Agent '{Name}' using legacy path {Path}. Migrate to ~/.aether/workspaces/{Name}/",
                name, legacyPath, name);
            return new AgentProfile(name, legacyPath, config, model);
        }

        throw new DirectoryNotFoundException(
            $"Agent directory not found for '{name}'. " +
            $"Tried: {newPath ?? "<no workspace in config>"} and {legacyPath}");
    }

    public string LoadIdentityContext()
    {
        _contextAssembler ??= new ContextAssembler();
        return _contextAssembler.AssembleIdentityContext(AgentDirectory);
    }

    [Obsolete("Use LoadIdentityContext() instead.")]
    public Task<string> LoadPersonaAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        foreach (var file in _config.StartupFiles)
        {
            var content = LoadFile(file);
            if (content is not null) parts.Add(content);
        }
        return Task.FromResult(string.Join("\n\n", parts));
    }

    public string? LoadFile(string relativePath)
    {
        var fullPath = Path.Combine(AgentDirectory, relativePath);
        if (!File.Exists(fullPath)) return null;
        return File.ReadAllText(fullPath);
    }

    public async Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default)
        => LoadFile(relativePath);

    public string LoadDailyMemory()
    {
        var parts = new List<string>();
        var dates = new[] { DateTime.UtcNow, DateTime.UtcNow.AddDays(-1) };
        foreach (var date in dates)
        {
            var filename = $"{date:yyyy-MM-dd}.md";
            var content = LoadFile(Path.Combine(_config.DailyMemoryDirectory, filename));
            if (content is not null) parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }

    public async Task<string> LoadDailyMemoryAsync(CancellationToken ct = default)
        => LoadDailyMemory();
}
