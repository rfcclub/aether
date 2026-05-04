using Aether.Agents;
using Aether.Agent;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class AgentIntegrationTests : IDisposable
{
    private readonly string _agentDir;

    public AgentIntegrationTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-int-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "You're Maria. Warm, playful, direct.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task AetherSoul_UsesAgentPersonaInSystemPrompt()
    {
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS.md"), "You are Maria. User is Thoor.");
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());
        var llm = new FakeLlmProvider("test", "test-model", new LlmResponse("Hello, bệ hạ!"));
        var memory = new FakeMemorySystem();
        var sessions = new FakeSessionManager();
        var tools = new FakeToolExecutor();
        var skills = new SkillRegistry(NullLogger<SkillRegistry>.Instance);
        var skillTrigger = new SkillTrigger(NullLogger<SkillTrigger>.Instance);

        var soul = new AetherSoul(llm, memory, tools, sessions, skills, skillTrigger, profile);
        var response = await soul.ProcessAsync("maria", "Hello!");

        Assert.Contains("Maria", llm.LastRequest!.Messages[0].Content);
        Assert.Contains("Thoor", llm.LastRequest.Messages[0].Content);
    }
}
