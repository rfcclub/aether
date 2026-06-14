using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Aether.Sessions;
using Aether.Data;
using Aether.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.Sessions;

public sealed class SessionCompactionServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AetherDb _db;
    private readonly SessionManager _sessionManager;

    public SessionCompactionServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aether-compaction-test-{Guid.NewGuid():N}.db");
        var schemaPath = FindSchemaPath();
        _db = new AetherDb(_dbPath, schemaPath);
        _db.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _sessionManager = new SessionManager(_db);
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

    private static IConfiguration CreateConfiguration(int triggerThreshold, int keepRecentCount)
    {
        var settings = new Dictionary<string, string?>
        {
            { "compaction:trigger_threshold", triggerThreshold.ToString() },
            { "compaction:keep_recent_count", keepRecentCount.ToString() }
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public async Task CompactSession_Skipped_WhenMessagesUnderOrEqualToThreshold()
    {
        // Arrange
        var config = CreateConfiguration(triggerThreshold: 5, keepRecentCount: 2);
        var fakeLlm = new FakeLlmProvider("fake", "fake-model", new LlmResponse("Summarized content"));
        var providerRouter = new ProviderRouter(
            new ILLMProvider[] { fakeLlm },
            new ProviderRoutingOptions { ProviderPriorities = new Dictionary<string, int> { ["fake"] = 1 } },
            _db,
            NullLogger<ProviderRouter>.Instance);

        var service = new SessionCompactionService(
            _sessionManager,
            providerRouter,
            _db,
            config,
            NullLogger<SessionCompactionService>.Instance);

        var session = await _sessionManager.GetOrCreateSessionAsync("test-group");
        for (int i = 1; i <= 4; i++)
        {
            await _sessionManager.AppendMessageAsync(session.Id, new SessionMessage("user", $"msg{i}", DateTimeOffset.UtcNow.AddMinutes(i)));
        }

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        service.EnqueueCompaction(session.Id);
        
        // Wait a brief moment to allow potential compaction processing
        await Task.Delay(200, cts.Token);
        await service.StopAsync(cts.Token);

        // Assert
        Assert.Equal(0, fakeLlm.CallCount);

        // Use a fresh SessionManager instance to bypass the in-memory cache
        var verifySessionManager = new SessionManager(_db);
        var history = await verifySessionManager.GetHistoryAsync(session.Id, maxTokens: 4000);
        Assert.Equal(4, history.Count);
        Assert.All(history, msg => Assert.NotEqual("system", msg.Role));
    }

    [Fact]
    public async Task CompactSession_RunsCorrectly_WhenMessagesOverThreshold()
    {
        // Arrange
        var config = CreateConfiguration(triggerThreshold: 5, keepRecentCount: 2);
        var fakeLlm = new FakeLlmProvider("fake", "fake-model", new LlmResponse("Summarized conversation"));
        var providerRouter = new ProviderRouter(
            new ILLMProvider[] { fakeLlm },
            new ProviderRoutingOptions { ProviderPriorities = new Dictionary<string, int> { ["fake"] = 1 } },
            _db,
            NullLogger<ProviderRouter>.Instance);

        var service = new SessionCompactionService(
            _sessionManager,
            providerRouter,
            _db,
            config,
            NullLogger<SessionCompactionService>.Instance);

        var session = await _sessionManager.GetOrCreateSessionAsync("test-group");
        // Append 6 messages (over the threshold of 5)
        for (int i = 1; i <= 6; i++)
        {
            await _sessionManager.AppendMessageAsync(session.Id, new SessionMessage(i % 2 == 0 ? "assistant" : "user", $"msg{i}", DateTimeOffset.UtcNow.AddMinutes(i)));
        }

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.StartAsync(cts.Token);
        service.EnqueueCompaction(session.Id);

        // Poll until LLM completes
        while (fakeLlm.CallCount == 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(50, cts.Token);
        }

        await service.StopAsync(cts.Token);

        // Assert
        Assert.Equal(1, fakeLlm.CallCount);

        // Use a fresh SessionManager instance to bypass the in-memory cache
        var verifySessionManager = new SessionManager(_db);
        var history = await verifySessionManager.GetHistoryAsync(session.Id, maxTokens: 4000);
        
        // Expecting 3 messages: 1 summary + 2 kept messages (msg5 and msg6)
        Assert.Equal(3, history.Count);

        // Verify summary exists
        var summaryMsg = history.FirstOrDefault(m => m.Role == "system");
        Assert.NotNull(summaryMsg);
        Assert.Contains("Summarized conversation", summaryMsg.Content);

        // Verify the 2 most recent messages are intact
        var keptMessages = history.Where(m => m.Role != "system").ToList();
        Assert.Equal(2, keptMessages.Count);
        Assert.Contains(keptMessages, m => m.Content == "msg5");
        Assert.Contains(keptMessages, m => m.Content == "msg6");

        // Verify older messages are deleted
        Assert.DoesNotContain(history, m => m.Content == "msg1");
        Assert.DoesNotContain(history, m => m.Content == "msg2");
        Assert.DoesNotContain(history, m => m.Content == "msg3");
        Assert.DoesNotContain(history, m => m.Content == "msg4");
    }

    [Fact]
    public async Task CompactSession_ConcurrentEnqueueing_IsSafe()
    {
        // Arrange
        var config = CreateConfiguration(triggerThreshold: 3, keepRecentCount: 1);
        var fakeLlm = new FakeLlmProvider("fake", "fake-model", new LlmResponse("Summarized batch"));
        var providerRouter = new ProviderRouter(
            new ILLMProvider[] { fakeLlm },
            new ProviderRoutingOptions { ProviderPriorities = new Dictionary<string, int> { ["fake"] = 1 } },
            _db,
            NullLogger<ProviderRouter>.Instance);

        var service = new SessionCompactionService(
            _sessionManager,
            providerRouter,
            _db,
            config,
            NullLogger<SessionCompactionService>.Instance);

        const int numSessions = 10;
        var sessions = new List<Session>();

        for (int i = 0; i < numSessions; i++)
        {
            var session = await _sessionManager.GetOrCreateSessionAsync($"session-{i}");
            // Append 4 messages (over threshold of 3)
            for (int j = 1; j <= 4; j++)
            {
                await _sessionManager.AppendMessageAsync(session.Id, new SessionMessage("user", $"msg-{i}-{j}", DateTimeOffset.UtcNow.AddMinutes(j)));
            }
            sessions.Add(session);
        }

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StartAsync(cts.Token);

        // Enqueue all compaction requests concurrently
        var enqueueTasks = sessions.Select(s => Task.Run(() => service.EnqueueCompaction(s.Id), cts.Token));
        await Task.WhenAll(enqueueTasks);

        // Poll until all 10 sessions have been compacted in the database
        var verifySessionManager = new SessionManager(_db);
        bool allCompacted = false;
        while (!allCompacted && !cts.IsCancellationRequested)
        {
            allCompacted = true;
            foreach (var s in sessions)
            {
                var history = await verifySessionManager.GetHistoryAsync(s.Id, maxTokens: 4000);
                if (history.Count != 2)
                {
                    allCompacted = false;
                    break;
                }
            }
            if (!allCompacted)
            {
                await Task.Delay(50, cts.Token);
            }
        }

        await service.StopAsync(cts.Token);

        // Assert
        Assert.Equal(numSessions, fakeLlm.CallCount);

        // Verify each session is correctly compacted
        foreach (var s in sessions)
        {
            var history = await verifySessionManager.GetHistoryAsync(s.Id, maxTokens: 4000);
            // 1 summary + 1 kept (msg-i-4) = 2 messages total
            Assert.Equal(2, history.Count);
            
            var summaryMsg = history.FirstOrDefault(m => m.Role == "system");
            Assert.NotNull(summaryMsg);
            
            var keptMsg = history.FirstOrDefault(m => m.Role != "system");
            Assert.NotNull(keptMsg);
            Assert.EndsWith("-4", keptMsg.Content);
        }
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // Ignore transient file locks during cleanup
            }
        }
    }
}
