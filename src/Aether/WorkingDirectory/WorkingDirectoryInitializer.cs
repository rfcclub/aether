using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Aether.WorkingDirectory;

public sealed class WorkingDirectoryInitializer : IHostedService
{
    private readonly string _aetherDir;
    private readonly ILogger<WorkingDirectoryInitializer> _logger;

    private static readonly string[] Subdirectories =
    {
        "identity", "agents", "workspaces", "store", "cron", "logs", "backups"
    };

    public WorkingDirectoryInitializer(string aetherDir, ILogger<WorkingDirectoryInitializer> logger)
    {
        _aetherDir = aetherDir;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_aetherDir))
        {
            _logger.LogInformation("Creating Aether working directory at {Path}", _aetherDir);
            Directory.CreateDirectory(_aetherDir);

            foreach (var subdir in Subdirectories)
                Directory.CreateDirectory(Path.Combine(_aetherDir, subdir));

            await CreateDeviceIdentityAsync();
        }
        else
        {
            foreach (var subdir in Subdirectories)
            {
                var path = Path.Combine(_aetherDir, subdir);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }

            var devicePath = Path.Combine(_aetherDir, "identity", "device.json");
            if (!File.Exists(devicePath))
                await CreateDeviceIdentityAsync();
        }
    }

    private async Task CreateDeviceIdentityAsync()
    {
        var devicePath = Path.Combine(_aetherDir, "identity", "device.json");
        var device = new
        {
            deviceId = Guid.NewGuid().ToString(),
            createdAt = DateTime.UtcNow.ToString("O"),
            version = "0.1.0"
        };
        var json = JsonSerializer.Serialize(device, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(devicePath, json);
        _logger.LogInformation("Device identity created at {Path}", devicePath);
    }
}
