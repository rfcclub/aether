using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aether.Providers;

public sealed class OpenRouterProvider : ILLMProvider
{
    private readonly HttpClient _client;
    private readonly OpenRouterOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OpenRouterProvider(HttpClient client, OpenRouterOptions options)
    {
        _client = client;
        _options = options;

        if (_client.BaseAddress is null)
        {
            _client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(new
        {
            model = _options.Model,
            messages = request.Messages.Select(message => new
            {
                role = message.Role,
                content = message.Content
            })
        }, options: JsonOptions);

        using var response = await _client.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"OpenRouter request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenRouter response did not contain assistant content.");
        }

        return new LlmResponse(content);
    }
}

public sealed record OpenRouterOptions(string ApiKey, string Model, string BaseUrl);
