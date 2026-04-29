namespace Aether.Agents;

/// <summary>
/// Bridges OC-format agent memory files (daily transcripts, MEMORY.md, task inbox/report).
/// Complements the existing IMemorySystem (SQLite/FTS5) with file-based agent memory.
/// </summary>
public sealed class AgentMemoryBridge
{
    private readonly string _agentDir;
    private readonly AgentConfig _config;

    public AgentMemoryBridge(string agentDir, AgentConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    public async Task AppendDailyMemoryAsync(string content, string sessionId)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        Directory.CreateDirectory(dailyDir);

        var filename = $"{DateTime.UtcNow:yyyy-MM-dd}.md";
        var filePath = Path.Combine(dailyDir, filename);

        var entry = $"\n## {DateTime.UtcNow:HH:mm:ss} UTC | session: {sessionId}\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    public async Task<string> ReadLongTermMemoryAsync(CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    public async Task WriteLongTermMemoryAsync(string content, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    public async Task<string> ReadTaskInboxAsync(CancellationToken ct = default)
    {
        if (_config.TaskInboxFile is null)
            return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.TaskInboxFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    public async Task WriteTaskReportAsync(string content, CancellationToken ct = default)
    {
        if (_config.TaskReportFile is null)
            return;
        var filePath = Path.Combine(_agentDir, _config.TaskReportFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    public async Task<string> ReadDreamsAsync(CancellationToken ct = default)
    {
        if (_config.Feofalls is null) return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.Feofalls.DreamsFile);
        if (!File.Exists(filePath)) return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    public async Task AppendDreamAsync(string content)
    {
        if (_config.Feofalls is null) return;
        var filePath = Path.Combine(_agentDir, _config.Feofalls.DreamsFile);
        var entry = $"\n---\n\n*{DateTime.UtcNow:MMMM dd, yyyy 'at' h:mm tt}*\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    public IReadOnlyList<string> GetMemoryFiles(DateTime? since = null)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        if (!Directory.Exists(dailyDir)) return Array.Empty<string>();
        return Directory.GetFiles(dailyDir, "*.md")
            .Where(f => !since.HasValue || File.GetLastWriteTimeUtc(f) >= since.Value)
            .OrderByDescending(f => f)
            .ToList();
    }
}
