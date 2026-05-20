using Aether.SelfImprovement;

namespace Aether.Tests.SelfImprovement;

public class BenchmarkReportParserTests
{
    [Fact]
    public void Parse_ValidSummary_ExtractsCounts()
    {
        var output = "Some logs... \nTest summary: total: 436, failed: 0, succeeded: 436, skipped: 0, duration: 41.4s\nMore logs...";
        var result = new BenchmarkResult(true, 0, output, "");

        var parsed = BenchmarkReportParser.Parse(result);

        Assert.Equal(436, parsed.TotalTests);
        Assert.Equal(436, parsed.PassedTests);
        Assert.Equal(0, parsed.FailedTests);
    }

    [Fact]
    public void Parse_FailingSummary_ExtractsCounts()
    {
        var output = "Test summary: total: 10, failed: 2, succeeded: 8, skipped: 0";
        var result = new BenchmarkResult(false, 1, output, "");

        var parsed = BenchmarkReportParser.Parse(result);

        Assert.Equal(10, parsed.TotalTests);
        Assert.Equal(8, parsed.PassedTests);
        Assert.Equal(2, parsed.FailedTests);
    }

    [Fact]
    public void Parse_MalformedSummary_ReturnsOriginal()
    {
        var output = "Total tests passed: 436";
        var result = new BenchmarkResult(true, 0, output, "");

        var parsed = BenchmarkReportParser.Parse(result);

        Assert.Null(parsed.TotalTests);
    }
}
