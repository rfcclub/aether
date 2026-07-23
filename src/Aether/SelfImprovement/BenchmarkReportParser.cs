using System.Text.RegularExpressions;

namespace Aether.SelfImprovement;

public static class BenchmarkReportParser
{
    private static readonly Regex StandardSummaryRegex = new Regex(
        @"Passed!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*\d+,\s*Total:\s*(?<total>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CustomSummaryRegex = new Regex(
        @"Test summary: total: (?<total>\d+), failed: (?<failed>\d+), succeeded: (?<passed>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static BenchmarkResult Parse(BenchmarkResult result)
    {
        if (string.IsNullOrEmpty(result.Output)) return result;

        // Try standard dotnet test output: "Passed! - Failed: 0, Passed: 5, Total: 5"
        var match = StandardSummaryRegex.Match(result.Output);
        if (match.Success)
        {
            return result with
            {
                TotalTests = int.Parse(match.Groups["total"].Value),
                PassedTests = int.Parse(match.Groups["passed"].Value),
                FailedTests = int.Parse(match.Groups["failed"].Value)
            };
        }

        // Fallback: old custom format: "Test summary: total: 436, failed: 0, succeeded: 436"
        match = CustomSummaryRegex.Match(result.Output);
        if (match.Success)
        {
            return result with
            {
                TotalTests = int.Parse(match.Groups["total"].Value),
                PassedTests = int.Parse(match.Groups["passed"].Value),
                FailedTests = int.Parse(match.Groups["failed"].Value)
            };
        }

        return result;
    }
}
