using Xunit;
using Aether.Tooling.DynamicTool;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.Tooling;

public class DynamicToolWatcherServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly DynamicToolWatcherService _watcher;

    public DynamicToolWatcherServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "aether-dyn-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _watcher = new DynamicToolWatcherService(_testDir, NullLogger<DynamicToolWatcherService>.Instance);
        _watcher.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _watcher.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _watcher.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public void Watcher_StartsEmpty()
    {
        Assert.Empty(_watcher.Tools);
    }
}