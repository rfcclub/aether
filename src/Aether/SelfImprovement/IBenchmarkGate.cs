namespace Aether.SelfImprovement;

public record BenchmarkResult(bool Passed, int ExitCode, string Output, string Error);

public interface IBenchmarkGate
{
    Task<BenchmarkResult> RunTestsAsync(CancellationToken ct = default);
}
