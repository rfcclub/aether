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
