using Aether.Providers;
using Aether.Agent;
using Aether.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class AetherSoulTests
{
    private static (AetherSoul soul, FakeLlmProvider llm, FakeToolExecutor tools, FakeSessionManager sessions) CreateSoul(string llmResponse = "ack")
    {
        var provider = new FakeLlmProvider("fake", "fake-model", new LlmResponse(llmResponse));
        var memory = new FakeMemorySystem();
        var tools = new FakeToolExecutor();
        var sessions = new FakeSessionManager();
        var skills = new SkillRegistry(NullLogger<SkillRegistry>.Instance);
        var trigger = new SkillTrigger(NullLogger<SkillTrigger>.Instance);
        var soul = new AetherSoul(provider, memory, tools, sessions, skills, trigger);
        return (soul, provider, tools, sessions);
    }

    [Fact]
    public async Task ProcessAsync_SimplePrompt_ReturnsResponse()
    {
        var (soul, llm, _, sessions) = CreateSoul("hello back");

        var response = await soul.ProcessAsync("main", "hello");

        Assert.Equal("hello back", response.Content);
        Assert.NotNull(llm.LastRequest);
        Assert.Contains(llm.LastRequest!.Messages, m => m.Role == "user" && m.Content == "hello");
        Assert.Equal(2, sessions.SavedMessages.Count);
    }

    [Fact]
    public async Task ProcessAsync_IncludesSystemPrompt()
    {
        var (soul, llm, _, _) = CreateSoul("ok");

        await soul.ProcessAsync("main", "test");

        Assert.Contains(llm.LastRequest!.Messages, m => m.Role == "system");
        Assert.Contains("Aether", llm.LastRequest.Messages[0].Content);
    }

    [Fact]
    public async Task ProcessAsync_IncludesToolDefinitions()
    {
        var (soul, llm, _, _) = CreateSoul("ok");

        await soul.ProcessAsync("main", "test");

        Assert.NotNull(llm.LastRequest!.Tools);
        Assert.Contains(llm.LastRequest.Tools!, t => t.Name == "read");
        Assert.Contains(llm.LastRequest.Tools!, t => t.Name == "bash");
        Assert.Contains(llm.LastRequest.Tools!, t => t.Name == "write");
        Assert.Contains(llm.LastRequest.Tools!, t => t.Name == "edit");
        Assert.Contains(llm.LastRequest.Tools!, t => t.Name == "glob");
        Assert.Contains(llm.LastRequest.Tools!, t => t.Name == "grep");
    }

    [Fact]
    public async Task ProcessAsync_ToolCall_LoopsUntilFinalResponse()
    {
        var provider = new MultiResponseProvider(
            new LlmResponse("", new[] { new LlmToolCall("call-1", "read", new Dictionary<string, string> { ["path"] = "f.txt" }) }),
            new LlmResponse("final answer"));
        var memory = new FakeMemorySystem();
        var tools = new FakeToolExecutor(new ToolResult(true, "file contents"));
        var sessions = new FakeSessionManager();
        var skills = new SkillRegistry(NullLogger<SkillRegistry>.Instance);
        var trigger = new SkillTrigger(NullLogger<SkillTrigger>.Instance);
        var soul = new AetherSoul(provider, memory, tools, sessions, skills, trigger);

        var response = await soul.ProcessAsync("main", "read file");

        Assert.Equal("final answer", response.Content);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Single(tools.Calls);
        Assert.Equal("read", tools.Calls[0].Name);
    }

    [Fact]
    public async Task ProcessAsync_SkillTrigger_InjectsSkillContext()
    {
        var provider = new FakeLlmProvider("fake", "fake-model", new LlmResponse("done"));
        var skills = new SkillRegistry(NullLogger<SkillRegistry>.Instance);
        skills.Register(new SkillDefinition("pdf", "process PDF documents", "", Array.Empty<string>(), false, "PDF skill body"));
        var trigger = new SkillTrigger(NullLogger<SkillTrigger>.Instance);
        var memory = new FakeMemorySystem();
        var soul = new AetherSoul(provider, memory, new FakeToolExecutor(), new FakeSessionManager(), skills, trigger);

        await soul.ProcessAsync("main", "merge these PDF files please");

        var system = provider.LastRequest!.Messages.First(m => m.Role == "system");
        Assert.Contains("Aether", system.Content);
    }
}
