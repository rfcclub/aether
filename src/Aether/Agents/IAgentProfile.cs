namespace Aether.Agents;

/// <summary>
/// Loads an agent's persona from a directory containing SOUL.md, USER.md, etc.
/// An agent directory maps directly to an OC workspace structure.
/// </summary>
public interface IAgentProfile
{
    string Name { get; }
    string AgentDirectory { get; }

    /// <summary>
    /// Load all startup files in the configured order and return the full
    /// persona context for system prompt injection.
    /// </summary>
    Task<string> LoadPersonaAsync(CancellationToken ct = default);

    /// <summary>
    /// Load a specific file from the agent directory.
    /// Returns null if the file does not exist.
    /// </summary>
    Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Load today's and yesterday's daily memory files (memory/YYYY-MM-DD.md).
    /// </summary>
    Task<string> LoadDailyMemoryAsync(CancellationToken ct = default);
}

/// <summary>
/// Configuration for an agent profile — which files to load at startup and in what order.
/// </summary>
public record AgentConfig
{
    public List<string> StartupFiles { get; init; } = new() { "AGENTS.md" };
    public string LongTermMemoryFile { get; init; } = "MEMORY.md";
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";
    public string DailyMemoryDirectory { get; init; } = "memory";
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";

    /// <summary>
    /// FEOFALLS cognitive architecture layer configuration.
    /// Null disables FEOFALLS boot contract and lifecycle features.
    /// </summary>
    public FeofallsConfig? Feofalls { get; init; }
}

/// <summary>
/// FEOFALLS cognitive layer paths — relative to agent directory.
/// Mirrors the FEOFALLS v1.9 architecture: 0_CONSTITUTION through 5_WORKING_STATE.
/// </summary>
public record FeofallsConfig
{
    /// <summary>0_CONSTITUTION — axioms, boundaries, red lines. Creator approval required for writes.</summary>
    public List<string> ConstitutionFiles { get; init; } = new() { "AGENTS_GUARD.md" };

    /// <summary>1_IDENTITY — SOUL.md, USER.md, IDENTITY.md. Who the agent is.</summary>
    public List<string> IdentityFiles { get; init; } = new() { "SOUL.md", "USER.md", "IDENTITY.md" };

    /// <summary>2_COGNITIVE — decision style, trusted heuristics.</summary>
    public List<string> CognitiveFiles { get; init; } = new() { "MEMORY.md" };

    /// <summary>3_LEARNING — episodic log, mistakes, signals, dream diary.</summary>
    public string EpisodicLogFile { get; init; } = "INTROSPECTION.md";
    public string MistakesFile { get; init; } = "MEMORY.md";
    public string DreamsFile { get; init; } = "DREAMS.md";
    public string CandidatesDirectory { get; init; } = "memory/dreaming";

    /// <summary>5_WORKING_STATE — active tasks, system state.</summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>Lifecycle thresholds in days.</summary>
    public int ActiveToDecayingDays { get; init; } = 60;
    public int DecayingToArchivedDays { get; init; } = 90;
}
