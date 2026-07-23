namespace Aether.Memory;

/// <summary>
/// Abstraction over memory operations used by self-improvement and session management.
/// Implemented by both FileMemory (legacy/simple) and SqliteMemorySystem (full).
/// </summary>
public interface IMemorySystem
{
    Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(DateTime since, CancellationToken ct = default);
    Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default);
    Task<string> GetDurableMemoryAsync(CancellationToken ct = default);
    Task ForceConsolidationAsync(CancellationToken ct = default);
    string LoadContext(string groupFolder);
    Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default);
    void AddToContext(string content, float priority = 0.5f);
    void CompactContext(int targetTokens);
    IReadOnlyList<ContextEntry> GetContext();
    Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default);
    Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default);
}
