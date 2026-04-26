using System.Text.Json;

namespace Aether.Memory;

/// <summary>
/// Aether Memory System — Three-layer persistence with bounded durable memory.
/// 
/// ARCHITECTURE:
///   Ephemeral (in-memory, ~4K tokens, auto-compacted)
///     ↓ (promotion on significance)
///   Working (SQLite + FTS5, searchable, auto-archived)
///     ↓ (consolidation pipeline)
///   Durable (MEMORY.md, 2,500 char hard limit, human-editable)
/// 
/// BOUNDED MEMORY: When durable reaches limit, agent MUST consolidate
/// or evict. Forces prioritization, prevents bloat.
/// 
/// TODO: Implement full consolidation pipeline, promotion heuristics,
/// and FTS5 search ranking.
/// </summary>
public interface IMemorySystem
{
    // Ephemeral layer
    void AddToContext(string content, float priority = 0.5f);
    void CompactContext(int targetTokens);
    IReadOnlyList<ContextEntry> GetContext();

    // Working layer (SQLite)
    Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default);
    Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default);
    Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default);

    // Durable layer (MEMORY.md)
    Task<string> GetDurableMemoryAsync(CancellationToken ct = default);
    Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default);
    Task ForceConsolidationAsync(CancellationToken ct = default);
}

public record ContextEntry(string Content, float Priority, DateTime AddedAt);
public record SearchResult(string SessionId, string Snippet, float Score, DateTime Timestamp);
public record SessionSummary(string Id, string AgentId, DateTime StartedAt, string? Summary, int MessageCount);

/// <summary>
/// Candidate for promotion to durable memory.
/// Requires: confidence >= 0.7, evidence >= 3
/// </summary>
public record PromotionCandidate(
    string Content,
    float Confidence,      // 0.0-1.0, min 0.7 for auto-promote
    int EvidenceCount,     // min 3
    string Source,         // "reflection", "recidivism", "user_flag"
    DateTime CreatedAt
);
