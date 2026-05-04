namespace Aether.Memory;

public record ContextEntry(string Content, float Priority, DateTime AddedAt);
public record SearchResult(string SessionId, string Snippet, float Score, DateTime Timestamp);
public record SessionSummary(string Id, string AgentId, DateTime StartedAt, string? Summary, int MessageCount);

public record PromotionCandidate(
    string Content,
    float Confidence,
    int EvidenceCount,
    string Source,
    DateTime CreatedAt
);
