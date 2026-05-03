namespace Aether.Memory;

public sealed class FileMemory : IMemorySystem
{
    private readonly string _groupsRoot;
    private readonly List<ContextEntry> _context = new();
    private readonly object _lock = new();

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

    // ── Ephemeral context ──

    public void AddToContext(string content, float priority = 0.5f)
    {
        if (string.IsNullOrEmpty(content)) return;
        lock (_lock)
        {
            _context.Add(new ContextEntry(content, priority, DateTime.UtcNow));
        }
    }

    public void CompactContext(int targetTokens)
    {
        lock (_lock)
        {
            if (targetTokens <= 0)
            {
                _context.Clear();
                return;
            }

            // Keep compacting until under target
            while (true)
            {
                var total = _context.Sum(e => EstimateTokens(e.Content));
                if (total <= targetTokens) break;

                // Remove lowest-priority, oldest entry first
                var toRemove = _context
                    .OrderBy(e => e.Priority)
                    .ThenBy(e => e.AddedAt)
                    .FirstOrDefault();
                if (toRemove is null) break;
                _context.Remove(toRemove);
            }
        }
    }

    public IReadOnlyList<ContextEntry> GetContext()
    {
        lock (_lock)
        {
            return _context.ToList();
        }
    }

    // ── Session layer (stubs) ──

    public Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());

    public Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<SessionSummary?>(null);

    public Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(Array.Empty<SessionSummary>());

    public Task<string> GetDurableMemoryAsync(CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task ForceConsolidationAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    // ── Helpers ──

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

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, text.Length / 4);
    }
}
