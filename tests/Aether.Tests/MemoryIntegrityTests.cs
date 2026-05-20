using Aether.Data;
using Aether.Memory;
using Aether.Agents;
using Aether.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class MemoryIntegrityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _agentDir;
    private readonly string _memoryFilePath;
    private readonly AetherDb _db;
    private readonly SqliteMemorySystem _sqliteMemory;
    private readonly AgentMemoryBridge _bridge;

    public MemoryIntegrityTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether_integrity_{id}.db");
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether_agent_{id}");
        _memoryFilePath = Path.Combine(_agentDir, "MEMORY.md");
        
        Directory.CreateDirectory(_agentDir);

        // Initialize DB
        var schemaPath = FindSchemaPath();
        _db = new AetherDb(_dbPath, schemaPath);
        _db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        _sqliteMemory = new SqliteMemorySystem(_dbPath, _memoryFilePath, NullLogger<SqliteMemorySystem>.Instance);
        _sqliteMemory.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        var config = new AgentConfig { DailyMemoryDirectory = "memory", LongTermMemoryFile = "MEMORY.md" };
        _bridge = new AgentMemoryBridge(_agentDir, config);
    }

    private static string FindSchemaPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aether", "Data", "Schema.sql"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new FileNotFoundException("Cannot find Schema.sql");
    }

    [Fact]
    public async Task DualWrite_PersistsToBothSystems()
    {
        var sessionId = await _sqliteMemory.CreateSessionAsync("test-agent");
        var content = "Important memory to save";

        // ACTION: This is what we want to harden - a unified call that writes to both
        await _sqliteMemory.AppendMessageAsync(sessionId, "assistant", content);
        await _bridge.AppendDailyMemoryAsync(content, sessionId);

        // VERIFY SQLite
        var session = await _sqliteMemory.GetSessionAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(1, session.MessageCount);

        // VERIFY Markdown
        var dailyFiles = _bridge.GetMemoryFiles();
        Assert.Single(dailyFiles);
        var fileContent = await File.ReadAllTextAsync(dailyFiles[0]);
        Assert.Contains(content, fileContent);
        Assert.Contains(sessionId, fileContent);
    }

    public void Dispose()
    {
        _sqliteMemory.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_agentDir)) Directory.Delete(_agentDir, recursive: true);
    }
}
