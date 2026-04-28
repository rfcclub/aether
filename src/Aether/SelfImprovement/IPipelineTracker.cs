using Aether.Memory;

namespace Aether.SelfImprovement;

public record TrackedCandidate(
    string Id,
    string CandidateHash,
    CandidateState State,
    string Source,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IPipelineTracker
{
    Task TrackAsync(PromotionCandidate candidate, CancellationToken ct = default);
    Task TransitionAsync(PromotionCandidate candidate, CandidateState newState, CancellationToken ct = default);
    Task<IReadOnlyList<TrackedCandidate>> GetCandidatesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TrackedCandidate>> GetByStateAsync(CandidateState state, CancellationToken ct = default);
}
