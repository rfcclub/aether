using Aether.Agents;
using Aether.Agent;
using Aether.Providers;
using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class FeofallsIntegrationTests : IDisposable
{
    private readonly string _agentDir;

    public FeofallsIntegrationTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-feofalls-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS_GUARD.md"), "Red lines: no exfil. trash > rm.");
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS.md"), "Execute, don't perform.");
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "You're Maria. Warm, playful, direct.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor. Call him bệ hạ.");
        File.WriteAllText(Path.Combine(_agentDir, "IDENTITY.md"), "Name: Maria. Vibe: warm.");
        File.WriteAllText(Path.Combine(_agentDir, "MEMORY.md"), "## Promoted\n- Trusted: always check inbox first.");
        File.WriteAllText(Path.Combine(_agentDir, "TASK_INBOX.md"), "- [ ] Review pending PRs");
        File.WriteAllText(Path.Combine(_agentDir, "HEARTBEAT.md"), "Check inbox. HEARTBEAT_OK.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task FullBootContract_InjectsConstitutionAndIdentityIntoSystemPrompt()
    {
        var feofallsConfig = new FeofallsConfig();
        var bootContract = new FeofallsBootContract(_agentDir, feofallsConfig);
        var profile = new AgentProfile("maria", _agentDir,
            new AgentConfig { StartupFiles = new() { "SOUL.md", "USER.md" }, Feofalls = feofallsConfig });
        var llm = new FakeLlmProvider("test", "test-model", new LlmResponse("Hello, bệ hạ!"));
        var memory = new FakeMemorySystem();
        var sessions = new FakeSessionManager();
        var tools = new FakeToolExecutor();
        var skills = new SkillRegistry(NullLogger<SkillRegistry>.Instance);
        var trigger = new SkillTrigger(NullLogger<SkillTrigger>.Instance);

        var soul = new AetherSoul(llm, memory, tools, sessions, skills, trigger, profile, bootContract);
        var response = await soul.ProcessAsync("maria", "Hello!");

        var systemPrompt = llm.LastRequest!.Messages[0].Content;
        Assert.Contains("no exfil", systemPrompt);
        Assert.Contains("Execute, don't perform", systemPrompt);
        Assert.Contains("You're Maria", systemPrompt);
        Assert.Contains("bệ hạ", systemPrompt);
        Assert.Contains("always check inbox", systemPrompt);
        Assert.Contains("Review pending PRs", systemPrompt);
    }

    [Fact]
    public async Task EpisodicLogger_RecordsSessionEndToEnd()
    {
        var feofallsConfig = new FeofallsConfig { EpisodicLogFile = "EPISODIC_LOG.md" };
        var logger = new EpisodicLogger(_agentDir, feofallsConfig);

        var id = await logger.AppendEpisodeAsync("session-42", "assistant",
            "Completed PR review. Found 2 issues in auth middleware.");

        var logPath = Path.Combine(_agentDir, "EPISODIC_LOG.md");
        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("session-42", content);
        Assert.Contains("auth middleware", content);
        Assert.Contains("type: episode", content);
    }

    [Fact]
    public async Task LifecycleMachine_IntegratesWithMemoryBridge()
    {
        var feofallsConfig = new FeofallsConfig();
        var fsm = new LifecycleStateMachine(feofallsConfig);

        var state = fsm.ComputeState(DateTime.UtcNow, DateTime.UtcNow, accessCount: 0);
        Assert.Equal(MemoryLifecycleState.Active, state);

        var oldState = fsm.ComputeState(
            DateTime.UtcNow.AddDays(-70),
            DateTime.UtcNow.AddDays(-65),
            accessCount: 1);
        Assert.Equal(MemoryLifecycleState.Decaying, oldState);
    }

    [Fact]
    public async Task WriteValidator_BlocksConstitutionWrite()
    {
        var feofallsConfig = new FeofallsConfig
        {
            ConstitutionFiles = new() { "AGENTS_GUARD.md" }
        };
        var validator = new WriteValidator(feofallsConfig);

        var result = validator.ValidateWrite("AGENTS_GUARD.md", FeofallsLayer.Constitution);

        Assert.False(result.Allowed);
        Assert.True(result.RequiresApproval);
    }
}
