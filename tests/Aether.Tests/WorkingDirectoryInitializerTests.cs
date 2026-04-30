using Aether.WorkingDirectory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class WorkingDirectoryInitializerTests : IDisposable
{
    private readonly string _tempHome;

    public WorkingDirectoryInitializerTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"aether_test_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
            Directory.Delete(_tempHome, recursive: true);
    }

    [Fact]
    public async Task First_run_creates_full_directory_tree()
    {
        var aetherDir = Path.Combine(_tempHome, ".aether");
        var init = new WorkingDirectoryInitializer(aetherDir, NullLogger<WorkingDirectoryInitializer>.Instance);

        await init.InitializeAsync(CancellationToken.None);

        Assert.True(Directory.Exists(Path.Combine(aetherDir, "identity")), "identity/ missing");
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "agents")), "agents/ missing");
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "workspaces")), "workspaces/ missing");
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "store")), "store/ missing");
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "cron")), "cron/ missing");
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "logs")), "logs/ missing");
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "backups")), "backups/ missing");
    }

    [Fact]
    public async Task Subsequent_runs_are_idempotent()
    {
        var aetherDir = Path.Combine(_tempHome, ".aether");
        var init = new WorkingDirectoryInitializer(aetherDir, NullLogger<WorkingDirectoryInitializer>.Instance);

        await init.InitializeAsync(CancellationToken.None);
        var creationTime = Directory.GetLastWriteTimeUtc(aetherDir);

        await Task.Delay(10); // ensure time difference
        await init.InitializeAsync(CancellationToken.None);
        var secondTime = Directory.GetLastWriteTimeUtc(aetherDir);

        Assert.True(Directory.Exists(aetherDir));
        // Root directory should not be re-created; subdirectories should remain
        Assert.True(Directory.Exists(Path.Combine(aetherDir, "identity")));
    }

    [Fact]
    public async Task Device_identity_file_created_on_first_run()
    {
        var aetherDir = Path.Combine(_tempHome, ".aether");
        var init = new WorkingDirectoryInitializer(aetherDir, NullLogger<WorkingDirectoryInitializer>.Instance);

        await init.InitializeAsync(CancellationToken.None);

        var devicePath = Path.Combine(aetherDir, "identity", "device.json");
        Assert.True(File.Exists(devicePath), "device.json missing");

        var json = await File.ReadAllTextAsync(devicePath);
        Assert.Contains("deviceId", json);
        Assert.Contains("createdAt", json);
        Assert.Contains("version", json);
    }

    [Fact]
    public async Task Device_identity_file_not_overwritten_on_restart()
    {
        var aetherDir = Path.Combine(_tempHome, ".aether");
        var init = new WorkingDirectoryInitializer(aetherDir, NullLogger<WorkingDirectoryInitializer>.Instance);

        await init.InitializeAsync(CancellationToken.None);
        var devicePath = Path.Combine(aetherDir, "identity", "device.json");
        var originalJson = await File.ReadAllTextAsync(devicePath);

        await Task.Delay(10);
        await init.InitializeAsync(CancellationToken.None);
        var afterRestartJson = await File.ReadAllTextAsync(devicePath);

        Assert.Equal(originalJson, afterRestartJson);
    }

    [Fact]
    public async Task Custom_aether_home_respected()
    {
        var customDir = Path.Combine(_tempHome, "custom_aether");
        var init = new WorkingDirectoryInitializer(customDir, NullLogger<WorkingDirectoryInitializer>.Instance);

        await init.InitializeAsync(CancellationToken.None);

        Assert.True(Directory.Exists(customDir));
        Assert.True(Directory.Exists(Path.Combine(customDir, "identity")));
    }

    [Fact]
    public async Task Agent_subdirectories_not_pre_created()
    {
        var aetherDir = Path.Combine(_tempHome, ".aether");
        var init = new WorkingDirectoryInitializer(aetherDir, NullLogger<WorkingDirectoryInitializer>.Instance);

        await init.InitializeAsync(CancellationToken.None);

        var agentsDir = Path.Combine(aetherDir, "agents");
        var workspacesDir = Path.Combine(aetherDir, "workspaces");
        Assert.Empty(Directory.GetDirectories(agentsDir));
        Assert.Empty(Directory.GetDirectories(workspacesDir));
    }
}
