using Aether.Agents;
using Aether.Agent;
using Aether.Providers;
using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class AgentHeartbeatServiceTests : IDisposable
{
    private readonly string _agentDir;

    public AgentHeartbeatServiceTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-hb-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task StartAsync_DoesNotThrow_WithNullHeartbeatFile()
    {
        var config = new AgentConfig { HeartbeatFile = null };
        var profile = new AgentProfile("maria", _agentDir, config);
        var soul = CreateSoul();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentHeartbeatService>();

        var service = new AgentHeartbeatService(profile, soul, config, logger, TimeSpan.FromMinutes(60));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await service.StopAsync(cts.Token);
    }

    [Fact]
    public async Task StartAsync_StartsWithValidHeartbeatFile()
    {
        File.WriteAllText(Path.Combine(_agentDir, "HEARTBEAT.md"), "- [x] Check inbox\nHEARTBEAT_OK");
        var config = new AgentConfig { HeartbeatFile = "HEARTBEAT.md" };
        var profile = new AgentProfile("maria", _agentDir, config);
        var soul = CreateSoul("HEARTBEAT_OK");
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentHeartbeatService>();

        var service = new AgentHeartbeatService(profile, soul, config, logger, TimeSpan.FromHours(24));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await service.StopAsync(cts.Token);
    }

    private static AetherSoul CreateSoul(string response = "ok")
    {
        var llm = new FakeLlmProvider("test", "test-model", new LlmResponse(response));
        return new AetherSoul(llm,
            new FakeMemorySystem(),
            new FakeToolExecutor(),
            new FakeSessionManager(),
            new SkillRegistry(NullLogger<SkillRegistry>.Instance),
            new SkillTrigger(NullLogger<SkillTrigger>.Instance),
            TestAgentProfile.NoOp());
    }
}
