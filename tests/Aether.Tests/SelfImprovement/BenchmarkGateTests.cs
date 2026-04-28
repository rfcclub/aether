using Aether.SelfImprovement;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.SelfImprovement;

public class BenchmarkGateTests
{
    private sealed class TestableBenchmarkGate : BenchmarkGate
    {
        private readonly BenchmarkResult _result;

        public TestableBenchmarkGate(BenchmarkResult result, int timeoutSeconds = 60)
            : base("test/path", timeoutSeconds, NullLogger<BenchmarkGate>.Instance)
        {
            _result = result;
        }

        protected override Task<BenchmarkResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class TimeoutBenchmarkGate : BenchmarkGate
    {
        public TimeoutBenchmarkGate(int timeoutSeconds = 1)
            : base("test/path", timeoutSeconds, NullLogger<BenchmarkGate>.Instance)
        {
        }

        protected override async Task<BenchmarkResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new BenchmarkResult(true, 0, "", "");
        }
    }

    [Fact]
    public async Task RunTestsAsync_ProcessExitsZero_ReturnsPassed()
    {
        var gate = new TestableBenchmarkGate(new BenchmarkResult(true, 0, "all tests passed", ""));
        var result = await gate.RunTestsAsync();
        Assert.True(result.Passed);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task RunTestsAsync_ProcessExitsNonZero_ReturnsFailed()
    {
        var gate = new TestableBenchmarkGate(new BenchmarkResult(false, 1, "", "test failed"));
        var result = await gate.RunTestsAsync();
        Assert.False(result.Passed);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task RunTestsAsync_Timeout_ReturnsFailed()
    {
        var gate = new TimeoutBenchmarkGate(timeoutSeconds: 1);
        var result = await gate.RunTestsAsync();
        Assert.False(result.Passed);
        Assert.Contains("Timed out", result.Error);
    }
}
