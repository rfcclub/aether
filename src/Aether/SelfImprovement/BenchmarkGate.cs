using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Aether.SelfImprovement;

public class BenchmarkGate 
{
    private readonly string _testProjectPath;
    private readonly int _timeoutSeconds;
    private readonly ILogger<BenchmarkGate> _logger;

    public BenchmarkGate(string testProjectPath, int timeoutSeconds, ILogger<BenchmarkGate> logger)
    {
        _testProjectPath = testProjectPath;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public virtual async Task<BenchmarkResult> RunTestsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

        try
        {
            var result = await RunProcessAsync("dotnet", $"test \"{_testProjectPath}\"", cts.Token);
            return result;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Benchmark timed out after {Timeout}s", _timeoutSeconds);
            return new BenchmarkResult(false, -1, "", $"Timed out after {_timeoutSeconds}s");
        }
    }

    protected virtual async Task<BenchmarkResult> RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new BenchmarkResult(false, -1, "", "Failed to start process");
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(ct);
        var output = await stdout;
        var error = await stderr;

        _logger.LogInformation("Benchmark exit code: {ExitCode}", process.ExitCode);

        return new BenchmarkResult(
            Passed: process.ExitCode == 0,
            ExitCode: process.ExitCode,
            Output: output,
            Error: error);
    }
}
