using System.Net;
using System.Net.Http.Json;
using Aether.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class TavilyWebSearchProviderTests
{
    [Fact]
    public async Task SuccessfulSearch_ReturnsResults()
    {
        var provider = CreateProvider(new FakeHandler(req =>
        {
            Assert.Contains("api.tavily.com", req.RequestUri!.Host);
            var body = new { results = new[] {
                new { title = "Result 1", url = "https://one.com", content = "First result" },
                new { title = "Result 2", url = "https://two.com", content = "Second result" },
            }};
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(body) };
        }));

        var results = await provider.SearchAsync("test query", 10, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("Result 1", results[0].Title);
        Assert.Equal("https://one.com", results[0].Url);
        Assert.Equal("First result", results[0].Snippet);
    }

    [Fact]
    public async Task EmptyResults_ReturnsEmptyList()
    {
        var provider = CreateProvider(new FakeHandler(_ =>
        {
            var body = new { results = Array.Empty<object>() };
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(body) };
        }));

        var results = await provider.SearchAsync("no results", 5, CancellationToken.None);
        Assert.Empty(results);
    }

    [Fact]
    public async Task RateLimited_Throws()
    {
        var provider = CreateProvider(new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchAsync("test", 5, CancellationToken.None));
        Assert.Contains("rate limited", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingApiKey_Throws()
    {
        var previous = Environment.GetEnvironmentVariable("TAVILY_API_KEY");
        Environment.SetEnvironmentVariable("TAVILY_API_KEY", null);
        try
        {
            var config = new ConfigurationBuilder().Build();
            var provider = new TavilyWebSearchProvider(
                new HttpClient(new FakeHandler(_ => new HttpResponseMessage())),
                config,
                NullLogger<TavilyWebSearchProvider>.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.SearchAsync("test", 5, CancellationToken.None));
            Assert.Contains("API key", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TAVILY_API_KEY", previous);
        }
    }

    [Fact]
    public async Task Unauthorized_Throws()
    {
        var provider = CreateProvider(new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchAsync("test", 5, CancellationToken.None));
        Assert.Contains("API key is invalid", ex.Message);
    }

    [Fact]
    public async Task NetworkError_ThrowsWithMessage()
    {
        var provider = CreateProvider(new FakeHandler(_ =>
            throw new HttpRequestException("Connection refused")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchAsync("test", 5, CancellationToken.None));
        Assert.Contains("Connection refused", ex.Message);
    }

    [Fact]
    public async Task UsageLimitExceeded_Throws()
    {
        var provider = CreateProvider(new FakeHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)432)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SearchAsync("test", 5, CancellationToken.None));
        Assert.Contains("usage limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ──

    private static TavilyWebSearchProvider CreateProvider(FakeHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["providers:tavily:api_key"] = "tvly-test-key",
            })
            .Build();

        return new TavilyWebSearchProvider(
            new HttpClient(handler),
            config,
            NullLogger<TavilyWebSearchProvider>.Instance);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
