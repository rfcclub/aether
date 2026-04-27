using Aether.Providers;

namespace Aether.Tests;

public class LlmMessageTests
{
    [Fact]
    public void System_CreatesSystemMessage()
    {
        var msg = LlmMessage.System("You are Aether.");
        Assert.Equal("system", msg.Role);
        Assert.Equal("You are Aether.", msg.Content);
        Assert.Null(msg.ToolCallId);
        Assert.Null(msg.ToolName);
        Assert.Null(msg.ToolCalls);
    }

    [Fact]
    public void User_CreatesUserMessage()
    {
        var msg = LlmMessage.User("hello");
        Assert.Equal("user", msg.Role);
        Assert.Equal("hello", msg.Content);
    }

    [Fact]
    public void AssistantToolCalls_SetsToolCalls()
    {
        var calls = new LlmToolCall[]
        {
            new("call-1", "read", new Dictionary<string, string> { ["path"] = "file.txt" })
        };
        var msg = LlmMessage.AssistantToolCalls("", calls);
        Assert.Equal("assistant", msg.Role);
        Assert.NotNull(msg.ToolCalls);
        Assert.Single(msg.ToolCalls);
        Assert.Equal("call-1", msg.ToolCalls![0].Id);
        Assert.Equal("read", msg.ToolCalls[0].Name);
        Assert.Equal("file.txt", msg.ToolCalls[0].Arguments["path"]);
    }

    [Fact]
    public void ToolResult_CreatesToolResultMessage()
    {
        var msg = LlmMessage.ToolResult("call-1", "read", "file contents");
        Assert.Equal("tool", msg.Role);
        Assert.Equal("call-1", msg.ToolCallId);
        Assert.Equal("read", msg.ToolName);
        Assert.Equal("file contents", msg.Content);
    }
}
