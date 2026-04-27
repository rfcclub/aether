using System.Net;
using Aether.Providers;

namespace Aether.Tests;

public class OpenRouterProviderTests
{
    [Fact]
    public async Task CompleteAsync_ValidResponse_ReturnsContent()
    {
        var handler = new FakeHttpHandler("""
            {"choices":[{"message":{"role":"assistant","content":"pong"}}]}
            """, HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var provider = new OpenRouterProvider(client, new OpenRouterOptions("key", "gpt-4", "https://openrouter.ai/api/v1"));

        var response = await provider.CompleteAsync(
            new LlmRequest(new[] { LlmMessage.User("ping") }),
            CancellationToken.None);

        Assert.Equal("pong", response.Content);
        Assert.Empty(response.ToolCalls!);
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("Bearer key", handler.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task CompleteAsync_WithToolCalls_ParsesThem()
    {
        var json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call-1\",\"type\":\"function\",\"function\":{\"name\":\"read\",\"arguments\":\"{\\\"path\\\":\\\"file.txt\\\"}\"}}]}}]}";
        var handler = new FakeHttpHandler(json);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var provider = new OpenRouterProvider(client, new OpenRouterOptions("key", "gpt-4", "https://openrouter.ai/api/v1"));

        var request = new LlmRequest(
            new[] { LlmMessage.User("read file") },
            new[] { new LlmTool("read", "Read a file", """{"type":"object"}""") });

        var response = await provider.CompleteAsync(request, CancellationToken.None);

        Assert.Equal("", response.Content);
        Assert.NotNull(response.ToolCalls);
        Assert.Single(response.ToolCalls);
        Assert.Equal("call-1", response.ToolCalls![0].Id);
        Assert.Equal("read", response.ToolCalls[0].Name);
        Assert.Equal("file.txt", response.ToolCalls[0].Arguments["path"]);
    }

    [Fact]
    public async Task CompleteAsync_WithToolResultMessages_SerializesThem()
    {
        var handler = new FakeHttpHandler("""
            {"choices":[{"message":{"role":"assistant","content":"done"}}]}
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var provider = new OpenRouterProvider(client, new OpenRouterOptions("key", "gpt-4", "https://openrouter.ai/api/v1"));

        var request = new LlmRequest(new[]
        {
            LlmMessage.User("read file"),
            LlmMessage.AssistantToolCalls("", new[] { new LlmToolCall("call-1", "read", new Dictionary<string, string> { ["path"] = "f.txt" }) }),
            LlmMessage.ToolResult("call-1", "read", "file contents")
        });

        await provider.CompleteAsync(request, CancellationToken.None);

        Assert.Contains("\"tool_call_id\":\"call-1\"", handler.LastBody);
        Assert.Contains("\"role\":\"tool\"", handler.LastBody);
    }

    [Fact]
    public async Task CompleteAsync_ErrorResponse_Throws()
    {
        var handler = new FakeHttpHandler("""{"error":{"message":"model unavailable"}}""", HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai/api/v1/") };
        var provider = new OpenRouterProvider(client, new OpenRouterOptions("key", "bad-model", "https://openrouter.ai/api/v1"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteAsync(new LlmRequest(new[] { LlmMessage.User("hi") }), CancellationToken.None));

        Assert.Contains("400", ex.Message);
        Assert.Contains("model unavailable", ex.Message);
    }

    [Fact]
    public void Properties_ReflectOptions()
    {
        using var client = new HttpClient();
        var provider = new OpenRouterProvider(client, new OpenRouterOptions("key", "claude-3-opus", "https://openrouter.ai/api/v1"));

        Assert.Equal("openrouter", provider.Name);
        Assert.Equal("claude-3-opus", provider.Model);
        Assert.True(provider.SupportsStreaming);
        Assert.True(provider.SupportsTools);
    }
}
