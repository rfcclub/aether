using Aether.Providers;
using Aether.Agent;
using Aether.Agents;
using Aether.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Aether.Tooling;

namespace Aether.Tests;

public class AetherSoulStressTests
{
    private static (AetherSoul soul, FakeLlmProvider llm, FakeToolExecutor tools) CreateSoul(string llmResponse = "ack")
    {
        var provider = new FakeLlmProvider("fake", "fake-model", new LlmResponse(llmResponse));
        var tools = new FakeToolExecutor();
        var soul = new AetherSoul(provider, tools, TestAgentProfile.NoOp());
        return (soul, provider, tools);
    }

    [Fact]
    public async Task ProcessAsync_EmptySystemPrompt_IncludesDefaultRules()
    {
        var provider = new FakeLlmProvider("fake", "fake-model", new LlmResponse("ok"));
        var tools = new FakeToolExecutor();
        // Create profile without identity to get empty system prompt
        // Use a non-existent directory to ensure empty identity
        var profile = new AgentProfile("test", "/tmp/non-existent-" + Guid.NewGuid(), new AgentConfig(), new AgentModelConfig());
        var soul = new AetherSoul(provider, tools, profile);

        var response = await soul.ProcessAsync("main", "hello");

        Assert.Equal("ok", response.Content);
        Assert.NotNull(provider.LastRequest);
        
        var firstMsg = provider.LastRequest.Messages[0];
        Assert.Equal("system", firstMsg.Role);
        // When identity is empty, it falls back to WorkingContext's default prompt
        Assert.Contains("You are Aether", firstMsg.Content);
        Assert.Contains("## Rules", firstMsg.Content);
        // Default prompt doesn't have the date, so we don't assert it here
    }

    [Fact]
    public async Task ProcessAsync_MaxToolIterations_ThrowsException()
    {
        // Simulate an LLM that ALWAYS returns a tool call
        var toolCall = new LlmToolCall("call-id", "read", new Dictionary<string, string> { ["path"] = "test.txt" });
        
        // We need a provider that keeps returning tool calls forever
        var provider = new InfiniteToolCallProvider(toolCall);
        var tools = new FakeToolExecutor(new Aether.Agent.ToolResult(true, "contents"));
        var soul = new AetherSoul(provider, tools, TestAgentProfile.NoOp());

        // Currently, AetherSoul throws an exception when the limit (64) is exceeded.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => soul.ProcessAsync("main", "keep calling tools"));
        Assert.Contains("exceeded 64 tool iterations", ex.Message);

        // The limit is 64.
        Assert.Equal(64, provider.CallCount);
        Assert.Equal(64, tools.Calls.Count);
    }

    [Fact]
    public async Task ProcessAsync_ToolExecutionError_PropagatesToContext()
    {
        var toolCall = new LlmToolCall("call-1", "read", new Dictionary<string, string> { ["path"] = "bad.txt" });
        var provider = new MultiResponseProvider(
            new LlmResponse("", new[] { toolCall }),
            new LlmResponse("it failed as expected"));
        
        // Tool returns Success = false. Output is the detailed error.
        var tools = new FakeToolExecutor(new Aether.Agent.ToolResult(false, "permission denied", "ACCESS_ERROR"));
        var soul = new AetherSoul(provider, tools, TestAgentProfile.NoOp());

        var response = await soul.ProcessAsync("main", "read bad file");

        Assert.Equal("it failed as expected", response.Content);
        // Second request should contain the error message
        Assert.Equal(2, provider.Requests.Count);
        var toolMessage = provider.Requests[1].Messages.First(m => m.Role == "tool");
        // Format: Tool failed: {Error}{NewLine}{Output}
        Assert.Contains("Tool failed: ACCESS_ERROR", toolMessage.Content);
        Assert.Contains("permission denied", toolMessage.Content);
    }

    [Fact]
    public async Task ProcessAsync_MalformedToolCall_ReturnsErrorToLlm()
    {
        // Simulate a malformed tool call where arguments are not valid JSON or missing
        // ParameterValidator should catch this if SchemaJson is present
        var toolCall = new LlmToolCall("call-1", "read", new Dictionary<string, string>()); // missing required "path"
        
        var provider = new MultiResponseProvider(
            new LlmResponse("", new[] { toolCall }),
            new LlmResponse("I'll fix the call"));
        
        var tools = new FakeToolExecutor();
        var soul = new AetherSoul(provider, tools, TestAgentProfile.NoOp());

        var response = await soul.ProcessAsync("main", "read file");

        // The loop should continue, returning the validation error to the LLM
        Assert.Equal(2, provider.Requests.Count);
        var toolMessage = provider.Requests[1].Messages.First(m => m.Role == "tool");
        Assert.Contains("Tool validation failed", toolMessage.Content);
    }
}

internal sealed class InfiniteToolCallProvider : ILLMProvider
{
    private readonly LlmToolCall _toolCall;
    public int CallCount { get; private set; }
    public LlmRequest? LastRequest { get; private set; }

    public InfiniteToolCallProvider(LlmToolCall toolCall)
    {
        _toolCall = toolCall;
    }

    public string Name => "infinite-tool";
    public string Model => "infinite-model";
    public bool SupportsStreaming => false;
    public bool SupportsTools => true;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;
        return Task.FromResult(new LlmResponse("", new[] { _toolCall }));
    }

    public IAsyncEnumerable<string> CompleteStreamingAsync(LlmRequest request, CancellationToken ct) 
        => throw new NotImplementedException();
    public IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(LlmRequest request, CancellationToken ct) 
        => throw new NotImplementedException();
    public Task<bool> HealthCheckAsync(CancellationToken ct) => Task.FromResult(true);
}
