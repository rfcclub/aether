using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class ResearchLinker
{
    private readonly string _workspacePath;
    private readonly MariaSqliteStore _sqlite;
    private readonly ILogger _logger;

    public ResearchLinker(string workspacePath, MariaSqliteStore sqlite, ILogger logger)
    {
        _workspacePath = workspacePath;
        _sqlite = sqlite;
        _logger = logger;
    }

    public async Task CreateLinkAsync(string memoryId, string researchTopic, CancellationToken ct = default)
    {
        _logger.LogInformation("Linking memory {MemoryId} to research topic: {Topic}", memoryId, researchTopic);
        
        // 1. Link in SQLite graph
        await _sqlite.CreateEdgeAsync(memoryId, $"research:{researchTopic}", "references", 1.0f, ct);

        // 2. Update research findings file
        var researchFile = Path.Combine(_workspacePath, "research", "research_findings.md");
        Directory.CreateDirectory(Path.GetDirectoryName(researchFile)!);
        
        var linkEntry = $"\n- Linked Memory [{DateTime.UtcNow:yyyy-MM-dd}]: {memoryId} | Topic: {researchTopic}\n";
        await File.AppendAllTextAsync(researchFile, linkEntry, ct);
    }

    public async Task<string?> GetRelevantResearchAsync(string query, CancellationToken ct = default)
    {
        // Simple file search for now
        var researchFile = Path.Combine(_workspacePath, "research", "research_findings.md");
        if (!File.Exists(researchFile)) return null;

        var content = await File.ReadAllTextAsync(researchFile, ct);
        return content.Contains(query, StringComparison.OrdinalIgnoreCase) ? content : null;
    }
}
