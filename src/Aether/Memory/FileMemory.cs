using Aether.Plugins;

namespace Aether.Memory;

public class FileMemory
{
    private readonly string _groupsRoot;
    private readonly HookEngine? _hooks;
    private readonly List<ContextEntry> _context = new();
    private readonly object _lock = new();

    protected FileMemory() { _groupsRoot = Path.GetTempPath(); }

    public FileMemory(string groupsRoot, HookEngine? hooks = null)
    {
        _groupsRoot = groupsRoot;
        _hooks = hooks;
    }

    public virtual string LoadContext(string groupFolder)
    {
        var parts = new List<string>();
        AddIfExists(parts, Path.Combine(_groupsRoot, "CLAUDE.md"), "Global");
        AddIfExists(parts, Path.Combine(_groupsRoot, groupFolder, "CLAUDE.md"), groupFolder);
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    public virtual async Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default)
        => LoadContext(groupFolder);

    // ── Ephemeral context ──

    public virtual void AddToContext(string content, float priority = 0.5f)
    {
        if (string.IsNullOrEmpty(content)) return;
        if (_hooks is not null)
        {
            var ctx = new OnMemoryWriteContext
            {
                MemoryLayer = "file",
                Content = content,
                Confidence = priority
            };
            var result = _hooks.RunAsync(HookPoint.OnMemoryWrite, ctx, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (!result.Success || ctx.Denied)
                return;
        }

        lock (_lock)
        {
            _context.Add(new ContextEntry(content, priority, DateTime.UtcNow));
        }
    }

    public virtual void CompactContext(int targetTokens)
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

    public virtual IReadOnlyList<ContextEntry> GetContext()
    {
        lock (_lock)
        {
            return _context.ToList();
        }
    }

    // ── Session layer (stubs) ──

    public virtual Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default)
        => Task.FromResult(Guid.NewGuid().ToString());

    public virtual Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default)
        => Task.CompletedTask;

    public virtual Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());

    public virtual Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult<SessionSummary?>(null);

    public virtual Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(DateTime since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(Array.Empty<SessionSummary>());

    public virtual Task<string> GetDurableMemoryAsync(CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public virtual Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default)
        => Task.FromResult(false);

    public virtual Task ForceConsolidationAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    // ── Helpers ──

    private static void AddIfExists(List<string> parts, string path, string label)
    {
        if (!File.Exists(path)) return;
        var content = File.ReadAllText(path);
        if (!string.IsNullOrWhiteSpace(content))
            parts.Add($"# {label} Context{Environment.NewLine}{content.Trim()}");
    }

    private static async Task AddIfExistsAsync(List<string> parts, string path, string label, CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        var content = await File.ReadAllTextAsync(path, ct);
        if (!string.IsNullOrWhiteSpace(content))
            parts.Add($"# {label} Context{Environment.NewLine}{content.Trim()}");
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, text.Length / 4);
    }
}
