namespace Aether.Memory;

public sealed class FileMemory : IMemorySystem
{
    private readonly string _groupsRoot;

    public FileMemory(string groupsRoot)
    {
        _groupsRoot = groupsRoot;
    }

    public async Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default)
    {
        var parts = new List<string>();

        await AddIfExistsAsync(parts, Path.Combine(_groupsRoot, "CLAUDE.md"), "Global", ct);
        await AddIfExistsAsync(parts, Path.Combine(_groupsRoot, groupFolder, "CLAUDE.md"), groupFolder, ct);

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    public void AddToContext(string content, float priority = 0.5f) { }
    public void CompactContext(int targetTokens) { }

    public IReadOnlyList<ContextEntry> GetContext() => Array.Empty<ContextEntry>();

    public Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());

    public Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<SessionSummary?>(null);

    public Task<string> GetDurableMemoryAsync(CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task ForceConsolidationAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    private static async Task AddIfExistsAsync(List<string> parts, string path, string label, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        if (!string.IsNullOrWhiteSpace(content))
        {
            parts.Add($"# {label} Context{Environment.NewLine}{content.Trim()}");
        }
    }
}
