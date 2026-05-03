using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly ILogger<TavilyWebSearchProvider> _logger;

    public string Name => "tavily";

    public TavilyWebSearchProvider(
        HttpClient http,
        IConfiguration configuration,
        ILogger<TavilyWebSearchProvider> logger)
    {
        _http = http;
        _logger = logger;

        _apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY")
                  ?? configuration["providers:tavily:api_key"];
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "Tavily API key not configured. Set TAVILY_API_KEY env var or providers:tavily:api_key in config.");
        }

        var body = new TavilyRequest
        {
            Query = query,
            MaxResults = Math.Min(limit, 20),
            SearchDepth = "basic",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = JsonContent.Create(body);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _http.SendAsync(request, cts.Token);

            switch (response.StatusCode)
            {
                case System.Net.HttpStatusCode.Unauthorized:
                    throw new InvalidOperationException(
                        "Tavily API key is invalid. Check your TAVILY_API_KEY.");
                case System.Net.HttpStatusCode.TooManyRequests:
                    throw new InvalidOperationException(
                        "Tavily API rate limited. Retry after a few seconds.");
                case (System.Net.HttpStatusCode)432:
                case (System.Net.HttpStatusCode)433:
                    throw new InvalidOperationException(
                        "Tavily API usage limit exceeded. Check your plan.");
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TavilyResponse>(
                cancellationToken: cts.Token);

            if (result?.Results is null || result.Results.Count == 0)
                return Array.Empty<WebSearchResult>();

            return result.Results
                .Select(r => new WebSearchResult(
                    r.Title ?? "Untitled",
                    r.Url ?? "",
                    r.Content ?? ""))
                .ToList();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Tavily API request timed out after 15 seconds.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Tavily API request failed");
            throw new InvalidOperationException(
                $"Tavily API request failed: {ex.Message}");
        }
    }

    private sealed class TavilyRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = "";

        [JsonPropertyName("max_results")]
        public int MaxResults { get; set; } = 10;

        [JsonPropertyName("search_depth")]
        public string SearchDepth { get; set; } = "basic";
    }

    private sealed class TavilyResponse
    {
        [JsonPropertyName("results")]
        public List<TavilyResult>? Results { get; set; }
    }

    private sealed class TavilyResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
