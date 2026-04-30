namespace Aether.Config;

public sealed record AgentEntryConfig
{
    public string Name { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public AgentModelConfig Model { get; init; } = new();
    public List<string> Bindings { get; init; } = new();
    public int? HeartbeatIntervalMinutes { get; init; }
    public bool Enabled { get; init; } = true;
    public string? DisplayName { get; init; }
    public string? Emoji { get; init; }
}
