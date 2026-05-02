using System.Text.Json.Serialization;

namespace Aether.Config;

/// <summary>
/// Root config that follows agent-spec/spec/config/agent-config.schema.json.
/// Each agent has one of these at {workspace}/.aether.json.
/// </summary>
public sealed record AgentSpecConfig
{
    public SpecAgentSection Agent { get; init; } = new();
    public SpecStorageSection Storage { get; init; } = new();
    public SpecRuntimeSection Runtime { get; init; } = new();
    public SpecSurfacesSection Surfaces { get; init; } = new();
    public Dictionary<string, SpecProviderEntry> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public SpecToolsSection Tools { get; init; } = new();
    public SpecPolicySection Policy { get; init; } = new();
    public Dictionary<string, SpecChannelEntry> Channels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public SpecLoggingSection Logging { get; init; } = new();
}

public sealed record SpecAgentSection
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public string? Emoji { get; init; }
}

public sealed record SpecStorageSection
{
    public string Home { get; init; } = string.Empty;
    public string Sessions { get; init; } = "sessions";
    public string Memory { get; init; } = "memory";
    public string Receipts { get; init; } = "receipts";
    public string Integrity { get; init; } = "_INTEGRITY";
}

public sealed record SpecRuntimeSection
{
    public int MaxConcurrentTurns { get; init; } = 4;
    public int TurnTimeoutSeconds { get; init; } = 300;
    public int ContextBudgetTokens { get; init; } = 32000;
    public int ToolOutputMaxBytes { get; init; } = 1048576;
}

public sealed record SpecSurfacesSection
{
    public SpecCliSurface Cli { get; init; } = new();
    public SpecGatewaySurface Gateway { get; init; } = new();
    public SpecAcpSurface Acp { get; init; } = new();
}

public sealed record SpecCliSurface
{
    public bool Enabled { get; init; } = true;
}

public sealed record SpecGatewaySurface
{
    public bool Enabled { get; init; }
    public string BindHost { get; init; } = "127.0.0.1";
    public int BindPort { get; init; } = 8542;
    public bool AllowRemote { get; init; }
    public string? AuthToken { get; init; }
    public int MaxRequestSizeBytes { get; init; } = 1048576;
}

public sealed record SpecAcpSurface
{
    public bool Enabled { get; init; }
    public string BindHost { get; init; } = "127.0.0.1";
    public int BindPort { get; init; } = 8543;
}

public sealed record SpecProviderEntry
{
    public string Type { get; init; } = "openai";
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public string Model { get; init; } = string.Empty;
    public int MaxTokens { get; init; } = 4096;
    public double Temperature { get; init; } = 0.7;
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record SpecToolsSection
{
    public SpecShellTool Shell { get; init; } = new();
    public SpecFileTool File { get; init; } = new();
}

public sealed record SpecShellTool
{
    public bool Enabled { get; init; }
    public string? Autonomy { get; init; }
    public List<string> AllowedCommands { get; init; } = new();
    public List<string> DeniedCommands { get; init; } = new();
    public int TimeoutSeconds { get; init; } = 60;
}

public sealed record SpecFileTool
{
    public bool Enabled { get; init; } = true;
    public string? Autonomy { get; init; }
    public bool AllowWrites { get; init; }
    public List<string> AllowedPaths { get; init; } = new();
    public List<string> DeniedPaths { get; init; } = new();
    public int MaxFileSizeBytes { get; init; } = 10485760;
}

public sealed record SpecPolicySection
{
    public string DefaultAutonomy { get; init; } = "supervised";
    public bool DenyByDefault { get; init; } = true;
    public string SandboxBackend { get; init; } = "direct";
    public List<SpecPolicyRule> Rules { get; init; } = new();
}

public sealed record SpecPolicyRule
{
    public string Name { get; init; } = string.Empty;
    public string? MatchCategory { get; init; }
    public string? MatchName { get; init; }
    public string? MatchTarget { get; init; }
    public bool? MatchNetwork { get; init; }
    public bool? MatchFileWrite { get; init; }
    public string? MatchAutonomy { get; init; }
    public bool Allow { get; init; }
    public bool NeedsApproval { get; init; }
    public string? SandboxOverride { get; init; }
}

public sealed record SpecChannelEntry
{
    public string Type { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public System.Text.Json.JsonElement Config { get; init; }
}

public sealed record SpecLoggingSection
{
    public string Level { get; init; } = "INFO";
    public string Format { get; init; } = "text";
    public bool RedactSecrets { get; init; } = true;
}
