using System.Text.RegularExpressions;

namespace Aether.SelfImprovement;

public static class BenchmarkReportParser
{
    private static readonly Regex SummaryRegex = new Regex(
        @"Test summary: total: (?<total>\d+), failed: (?<failed>\d+), succeeded: (?<passed>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static BenchmarkResult Parse(BenchmarkResult result)
    {
        if (string.IsNullOrEmpty(result.Output)) return result;

        var match = SummaryRegex.Match(result.Output);
        if (match.Success)
        {
            return result with
            {
                TotalTests = int.Parse(match.Groups["total"].Value),
                PassedTests = int.Parse(match.Groups["passed"].Value),
                FailedTests = int.Parse(match.Groups["failed"].Value)
            };
        }

        // Fallback for different dotnet test output formats if needed
        return result;
    }
}
