using Xunit;
using Aether.Config;

namespace Aether.Tests;

public class TuiStartupTests
{
    [Fact]
    public void ParseAgentName_ShouldReturnAgentName_WhenLongFlagProvided()
    {
        var args = new[] { "--agent", "aura" };
        var name = TuiArgs.ParseAgentName(args);
        Assert.Equal("aura", name);
    }

    [Fact]
    public void ParseAgentName_ShouldReturnAgentName_WhenShortFlagProvided()
    {
        var args = new[] { "-a", "vesta" };
        var name = TuiArgs.ParseAgentName(args);
        Assert.Equal("vesta", name);
    }

    [Fact]
    public void ParseAgentName_ShouldReturnNull_WhenFlagMissing()
    {
        var args = new[] { "--url", "ws://localhost:5099/ws" };
        var name = TuiArgs.ParseAgentName(args);
        Assert.Null(name);
    }

    [Fact]
    public void ParseAgentName_ShouldReturnNull_WhenValueIsMissing()
    {
        var args = new[] { "--agent" };
        var name = TuiArgs.ParseAgentName(args);
        Assert.Null(name);
    }
}
