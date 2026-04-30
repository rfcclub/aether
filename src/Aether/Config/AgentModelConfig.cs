namespace Aether.Config;

public sealed record AgentModelConfig
{
    public string? Primary { get; init; }
    public List<string> Fallbacks { get; init; } = new();
    public Dictionary<string, ModelOverrideConfig> Overrides { get; init; } = new();
}

public sealed record ModelOverrideConfig
{
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
}
