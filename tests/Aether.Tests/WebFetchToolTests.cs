using System.Net;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class WebFetchToolTests
{
    [Fact]
    public async Task SuccessfulFetch_ReturnsText()
    {
        var tool = CreateTool(new FakeHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent("<html><body><p>Hello World</p></body></html>",
                System.Text.Encoding.UTF8, "text/html");
            return resp;
        }));

        var result = await tool.ExecuteAsync("https://example.com", CancellationToken.None);

        Assert.Contains("Hello World", result);
    }

    [Fact]
    public async Task NonHttpUrl_Throws()
    {
        var tool = CreateTool(new FakeHandler(_ => new HttpResponseMessage()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.ExecuteAsync("file:///etc/passwd", CancellationToken.None));
        Assert.Contains("only http and https", ex.Message);
    }

    [Fact]
    public async Task InvalidUrl_Throws()
    {
        var tool = CreateTool(new FakeHandler(_ => new HttpResponseMessage()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.ExecuteAsync("not a url", CancellationToken.None));
        Assert.Contains("invalid URL", ex.Message);
    }

    [Fact]
    public async Task ScriptTagsAreStripped()
    {
        var tool = CreateTool(new FakeHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent(
                "<html><body><p>Good</p><script>evil()</script></body></html>",
                System.Text.Encoding.UTF8, "text/html");
            return resp;
        }));

        var result = await tool.ExecuteAsync("https://example.com", CancellationToken.None);

        Assert.DoesNotContain("evil()", result);
        Assert.Contains("Good", result);
    }

    [Fact]
    public async Task ResponseTooLarge_Throws()
    {
        var tool = CreateTool(new FakeHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content.Headers.ContentLength = 6 * 1024 * 1024; // 6MB
            return resp;
        }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.ExecuteAsync("https://example.com", CancellationToken.None));
        Assert.Contains("exceeds 5MB", ex.Message);
    }

    [Fact]
    public async Task NetworkError_Throws()
    {
        var tool = CreateTool(new FakeHandler(_ =>
            throw new HttpRequestException("Connection refused")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tool.ExecuteAsync("https://example.com", CancellationToken.None));
        Assert.Contains("Connection refused", ex.Message);
    }

    // ── Helpers ──

    private static WebFetchTool CreateTool(FakeHandler handler)
    {
        var tool = new WebFetchTool(new HttpClient(handler), NullLogger<WebFetchTool>.Instance);
        tool.IsPrivateHostAsync = (_, _) => Task.FromResult(false); // bypass SSRF check
        return tool;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // DNS resolution would happen in real code; skip in tests
            return Task.FromResult(_handler(request));
        }
    }
}
