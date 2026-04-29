namespace Aether.Agents;

/// <summary>
/// Appends session events to FEOFALLS 3_LEARNING layer with canonical schema.
/// </summary>
public sealed class EpisodicLogger
{
    private readonly string _agentDir;
    private readonly FeofallsConfig _config;
    private int _sequence;

    public EpisodicLogger(string agentDir, FeofallsConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    public Task<string> AppendEpisodeAsync(string sessionId, string actor, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.EpisodicLogFile, "episode", sessionId, summary, tags);
    }

    public Task<string> AppendMistakeAsync(string sessionId, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.MistakesFile, "mistake", sessionId, summary, tags);
    }

    private async Task<string> AppendEntryAsync(string relativePath, string type, string sessionId,
        string summary, Dictionary<string, string>? tags)
    {
        var date = DateTime.UtcNow;
        var seq = Interlocked.Increment(ref _sequence);
        var id = $"mem_{date:yyyyMMdd}_{seq:D3}";

        var tagStr = tags is { Count: > 0 }
            ? string.Join(", ", tags.Select(kv => kv.Key))
            : "";

        var entry = $"""

---
id: {id}
type: {type}
source: session
session: {sessionId}
timestamp: {date:O}
confidence: 0.50
evidence_count: 1
tags: [{tagStr}]
links: []
status: candidate
---
{summary}

""";

        var fullPath = Path.Combine(_agentDir, relativePath);
        await File.AppendAllTextAsync(fullPath, entry);
        return id;
    }
}
