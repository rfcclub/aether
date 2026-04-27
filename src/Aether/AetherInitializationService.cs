using Aether.Data;
using Aether.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether;

public sealed class AetherInitializationService : IHostedService
{
    private readonly AetherDb _db;
    private readonly IMemorySystem _memory;
    private readonly ILogger<AetherInitializationService> _logger;

    public AetherInitializationService(
        AetherDb db,
        IMemorySystem memory,
        ILogger<AetherInitializationService> logger)
    {
        _db = db;
        _memory = memory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Aether initialization starting...");

        _logger.LogInformation("Initializing database schema...");
        await _db.InitializeAsync(ct);
        _logger.LogInformation("Database schema initialized.");

        if (_memory is SqliteMemorySystem sqliteMemory)
        {
            _logger.LogInformation("Initializing memory system...");
            await sqliteMemory.InitializeAsync(ct);
            _logger.LogInformation("Memory system initialized.");
        }
        else
        {
            _logger.LogWarning("Memory system is not SqliteMemorySystem - skipping initialization.");
        }

        _logger.LogInformation("Aether initialization complete.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}