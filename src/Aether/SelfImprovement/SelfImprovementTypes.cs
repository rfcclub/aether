using Aether.Memory;

namespace Aether.SelfImprovement;

public record BenchmarkResult(
    bool Passed, 
    int ExitCode, 
    string Output, 
    string Error,
    int? TotalTests = null,
    int? PassedTests = null,
    int? FailedTests = null);
public record TrackedCandidate(
    string Id,
    string CandidateHash,
    CandidateState State,
    string Source,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);
