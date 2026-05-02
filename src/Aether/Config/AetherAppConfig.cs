namespace Aether.Config;

/// <summary>
/// Aether's multi-agent manager config (~/.aether/config.json).
/// Agent-specific config follows agent-spec/spec/config/agent-config.schema.json
/// at each agent's {workspace}/.aether.json (see SpecContracts.cs).
/// </summary>
public sealed record AetherAppConfig
{
    /// <summary>Global provider defaults — merged into each agent's spec config.</summary>
    public Dictionary<string, SpecProviderEntry> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registered agents.</summary>
    public Dictionary<string, AgentEntryConfig> Agents { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-agent spec configs loaded from workspace.</summary>
    public Dictionary<string, AgentSpecConfig> AgentSpecs { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public MetaSection Meta { get; init; } = new();
    public WizardSection Wizard { get; init; } = new();
}

public sealed record MetaSection
{
    public string? LastTouchedVersion { get; init; }
}

public sealed record WizardSection
{
    public string? LastRunAt { get; init; }
    public string? LastRunVersion { get; init; }
    public string? LastRunCommand { get; init; }
}
