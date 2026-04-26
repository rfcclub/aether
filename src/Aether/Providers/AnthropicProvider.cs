using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aether.Providers;

/// <summary>
/// Anthropic provider - Claude via Anthropic API.
/// Safety tier for sensitive tasks.
/// </summary>
public sealed class AnthropicProvider : ILLMProvider
{
    private readonly HttpClient _client;
    private readonly AnthropicOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "anthropic";
    public string Model => _options.Model;
    public bool SupportsStreaming => true;
    public bool SupportsTools => true;

    public AnthropicProvider(HttpClient client, AnthropicOptions options)
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
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var body = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = request.Messages.Select(ToAnthropicMessage).ToArray(),
            ["max_tokens"] = 4096
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(ToAnthropicTool).ToArray();
            body["tool_choice"] = new { type = "auto" };
        }

        httpRequest.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _client.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Anthropic request failed with HTTP {(int)response.StatusCode}. Response: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = document.RootElement;

        // Extract content
        var content = "";
        var toolCalls = new List<LlmToolCall>();

        if (root.TryGetProperty("content", out var contentArray))
        {
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "text")
                {
                    content = block.GetProperty("text").GetString() ?? "";
                }
                else if (block.TryGetProperty("type", out var toolType) && toolType.GetString() == "tool_use")
                {
                    var id = block.GetProperty("id").GetString() ?? "";
                    var name = block.GetProperty("name").GetString() ?? "";
                    var inputJson = block.GetProperty("input").GetRawText();
                    var input = JsonSerializer.Deserialize<Dictionary<string, string>>(inputJson, JsonOptions)
                        ?? new Dictionary<string, string>();

                    toolCalls.Add(new LlmToolCall(id, name, input));
                }
            }
        }

        // Extract token usage from headers
        if (response.Headers.TryGetValues("x-consumer-token-usage", out var usageHeaders))
        {
            var usage = usageHeaders.FirstOrDefault();
            // Token usage logged for cost tracking
            _ = usage; // Used by caller via response headers
        }

        return new LlmResponse(content, toolCalls);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "messages");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model = _options.Model,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 1
            };

            request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await _client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static object ToAnthropicMessage(LlmMessage message)
    {
        if (message.Role == "tool")
        {
            return new
            {
                role = "user",
                content = new[]
                {
                    new { type = "tool_result", tool_use_id = message.ToolCallId, content = message.Content }
                }
            };
        }

        if (message.Role == "assistant" && message.ToolCalls is { Count: > 0 })
        {
            var content = new List<object>();
            if (!string.IsNullOrEmpty(message.Content))
            {
                content.Add(new { type = "text", text = message.Content });
            }
            foreach (var call in message.ToolCalls)
            {
                content.Add(new
                {
                    type = "tool_use",
                    id = call.Id,
                    name = call.Name,
                    input = JsonSerializer.Deserialize<Dictionary<string, object>>(call.Arguments.ToString() ?? "{}", JsonOptions)
                        ?? new Dictionary<string, object>()
                });
            }
            return new { role = "assistant", content };
        }

        return new
        {
            role = message.Role == "system" ? "user" : message.Role,
            content = message.Content
        };
    }

    private static object ToAnthropicTool(LlmTool tool)
    {
        using var parameters = JsonDocument.Parse(tool.ParametersJson);
        return new
        {
            name = tool.Name,
            description = tool.Description,
            input_schema = parameters.RootElement.Clone()
        };
    }
}

public sealed record AnthropicOptions(string ApiKey, string Model, string BaseUrl);