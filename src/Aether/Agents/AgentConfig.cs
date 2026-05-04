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
    public List<string> ConstitutionFiles { get; init; } = new() { "AGENTS_GUARD.md" };
    public List<string> IdentityFiles { get; init; } = new() { "SOUL.md", "USER.md", "IDENTITY.md" };
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
}
