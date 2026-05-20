using Aether.Data;
using Aether.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether;

public sealed class AetherInitializationService : IHostedService
{
    private readonly AetherDb _db;
    private readonly FileMemory _memory;
    private readonly SqliteMemorySystem _sqliteMemory;
    private readonly ILogger<AetherInitializationService> _logger;

    public AetherInitializationService(
        AetherDb db,
        FileMemory memory,
        SqliteMemorySystem sqliteMemory,
        ILogger<AetherInitializationService> logger)
    {
        _db = db;
        _memory = memory;
        _sqliteMemory = sqliteMemory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Aether initialization starting...");

        _logger.LogInformation("Initializing database schema...");
        await _db.InitializeAsync(ct);
        
        _logger.LogInformation("Initializing SQLite memory system...");
        await _sqliteMemory.InitializeAsync(ct);

        _logger.LogInformation("Aether initialization complete.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}