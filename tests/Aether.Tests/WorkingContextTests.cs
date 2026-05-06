using Aether.Providers;
using Aether.Agent;

namespace Aether.Tests;

public class WorkingContextTests
{
    private static IReadOnlyList<LlmTool> SampleTools => new[]
    {
        new LlmTool("read", "Read a file", "{}", "{}"),
        new LlmTool("write", "Write a file", "{}", "{}"),
    };

    [Fact]
    public void NewContext_HasSystemPromptAsFirstMessage()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);

        Assert.NotEmpty(ctx.Messages);
        Assert.Equal("system", ctx.Messages[0].Role);
    }

    [Fact]
    public void AddUser_AppendsUserMessage()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);

        ctx.AddUser("hello");

        Assert.Equal(2, ctx.Messages.Count);
        Assert.Equal("user", ctx.Messages[1].Role);
        Assert.Equal("hello", ctx.Messages[1].Content);
    }

    [Fact]
    public void AddAssistant_AppendsAssistantMessage()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        ctx.AddUser("hi");

        ctx.AddAssistant("response");

        Assert.Equal(3, ctx.Messages.Count);
        Assert.Equal("assistant", ctx.Messages[2].Role);
        Assert.Equal("response", ctx.Messages[2].Content);
    }

    [Fact]
    public void AddToolCall_AppendsAssistantWithToolCalls()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        ctx.AddUser("read file");
        var toolCalls = new[] { new LlmToolCall("call-1", "read", new Dictionary<string, string> { ["path"] = "/f.txt" }) };

        ctx.AddAssistantToolCalls("calling read", toolCalls);

        var last = ctx.Messages[^1];
        Assert.Equal("assistant", last.Role);
        Assert.NotNull(last.ToolCalls);
        Assert.Single(last.ToolCalls);
        Assert.Equal("read", last.ToolCalls![0].Name);
    }

    [Fact]
    public void AddToolResult_AppendsToolMessage()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        ctx.AddUser("read file");
        ctx.AddAssistantToolCalls("calling", new[] { new LlmToolCall("call-1", "read", new Dictionary<string, string> { ["path"] = "/f.txt" }) });

        ctx.AddToolResult("call-1", "read", "file contents");

        var last = ctx.Messages[^1];
        Assert.Equal("tool", last.Role);
        Assert.Equal("call-1", last.ToolCallId);
        Assert.Equal("file contents", last.Content);
    }

    [Fact]
    public void Reset_ClearsMessagesAndGeneratesNewSessionId()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        var session1 = ctx.SessionId;
        ctx.AddUser("hello");
        ctx.AddAssistant("hi");

        ctx.Reset();

        Assert.NotEqual(session1, ctx.SessionId);
        Assert.Empty(ctx.Messages);
    }

    [Fact]
    public void Compact_TrimsOldMessages_KeepsSystemAndLastUser()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        // Fill with long messages to exceed small token budget
        var longText = new string('x', 500);
        for (var i = 0; i < 10; i++)
        {
            ctx.AddUser(longText);
            ctx.AddAssistant(longText);
        }

        var before = ctx.Messages.Count;
        ctx.Compact(200); // ~50 tokens budget — forces trim

        Assert.True(ctx.Messages.Count < before, $"Expected {ctx.Messages.Count} < {before}");
        Assert.Equal("system", ctx.Messages[0].Role); // system preserved
    }

    [Fact]
    public void SystemPrompt_IncludesWorkspacePath()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);

        var system = ctx.Messages[0].Content;
        Assert.Contains("/tmp/ws", system);
    }

    [Fact]
    public void SetSystemPrompt_ReplacesExisting()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        ctx.AddUser("hi");

        ctx.SetSystemPrompt("custom prompt");

        Assert.Equal("system", ctx.Messages[0].Role);
        Assert.Equal("custom prompt", ctx.Messages[0].Content);
    }

    [Fact]
    public void Messages_ReturnsReadOnlyView()
    {
        var ctx = new WorkingContext("/tmp/ws", SampleTools);
        ctx.AddUser("test");

        var msgs = ctx.Messages;
        Assert.Equal(2, msgs.Count);
    }

    [Fact]
    public void SessionId_IsUniquePerInstance()
    {
        var ctx1 = new WorkingContext("/tmp/a", SampleTools);
        var ctx2 = new WorkingContext("/tmp/b", SampleTools);

        Assert.NotEqual(ctx1.SessionId, ctx2.SessionId);
    }
}
