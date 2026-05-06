namespace Aether.Agents;

public record AgentConfig
{
    public List<string> StartupFiles { get; init; } = new() { "AGENTS.md" };
    public string LongTermMemoryFile { get; init; } = "MEMORY.md";
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";
    public string DailyMemoryDirectory { get; init; } = "memory";
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";
    public BootConfig? Boot { get; init; }
}

public record BootConfig
{
    [Obsolete("ConstitutionFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> ConstitutionFiles { get; init; } = new() { "AGENTS_GUARD.md" };

    [Obsolete("IdentityFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> IdentityFiles { get; init; } = new() { "SOUL.md", "USER.md", "IDENTITY.md" };

    [Obsolete("CognitiveFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> CognitiveFiles { get; init; } = new() { "MEMORY.md" };

    public string EpisodicLogFile { get; init; } = "INTROSPECTION.md";
    public string MistakesFile { get; init; } = "MEMORY.md";
    public string DreamsFile { get; init; } = "DREAMS.md";
    public string CandidatesDirectory { get; init; } = "memory/dreaming";
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";
    public int ActiveToDecayingDays { get; init; } = 60;
    public int DecayingToArchivedDays { get; init; } = 90;

    /// <summary>
    /// Merge deprecated file lists into a single set for backward compatibility.
    /// When these fields have values, they are included in discovery but receive no semantic labeling.
    /// </summary>
    public HashSet<string> GetLegacyFileSet()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ConstitutionFiles) files.Add(f);
        foreach (var f in IdentityFiles) files.Add(f);
        foreach (var f in CognitiveFiles) files.Add(f);
        return files;
    }
}
