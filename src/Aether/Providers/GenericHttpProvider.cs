using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aether.Providers;

/// <summary>
/// Generic HTTP provider for custom/unknown endpoints.
/// Supports configurable base URL, endpoint, auth, and response parsing.
/// </summary>
public sealed class GenericHttpProvider : ILLMProvider
{
    private readonly HttpClient _client;
    private readonly GenericHttpOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => _options.Name;
    public string Model => _options.Model;
    public bool SupportsStreaming => false;
    public bool SupportsTools => true;

    public GenericHttpProvider(HttpClient client, GenericHttpOptions options)
    {
        _client = client;
        _options = options;

        if (_client.BaseAddress is null && !string.IsNullOrEmpty(_options.BaseUrl))
        {
            _client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);

        // Auth
        if (!string.IsNullOrEmpty(_options.AuthHeader))
        {
            var parts = _options.AuthHeader.Split(':', 2);
            if (parts.Length == 2)
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue(parts[0], parts[1]);
            }
            else if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                // AuthHeader is a scheme name (e.g. "Bearer") — create proper Authorization header
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthHeader, _options.ApiKey);
            }
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = BuildMessages(request)
        };

        // Include tools if supported and provided
        if (SupportsTools && request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<object>(t.ParametersJson)
                }
            }).ToArray();
            body["tool_choice"] = "auto";
        }

        httpRequest.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _client.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"GenericHttp request failed with HTTP {(int)response.StatusCode}. Response: {errorBody}");
        }

        return await ParseResponseAsync(response, ct);
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return response.Content;
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return new StreamEvent.TextToken(response.Content);
        yield return new StreamEvent.Response(response);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "health");
            if (!string.IsNullOrEmpty(_options.AuthHeader))
            {
                var parts = _options.AuthHeader.Split(':', 2);
                if (parts.Length == 2)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(parts[0], parts[1]);
                }
            }

            using var response = await _client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<LlmResponse> ParseResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;

        // Try OpenAI-style response
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            var message = choices[0].GetProperty("message");
            var content = "";
            if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null)
            {
                content = contentElement.GetString() ?? "";
            }

            // Parse tool calls if present
            List<LlmToolCall>? toolCalls = null;
            if (message.TryGetProperty("tool_calls", out var toolCallsElement) &&
                toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                toolCalls = new List<LlmToolCall>();
                foreach (var tc in toolCallsElement.EnumerateArray())
                {
                    var fn = tc.GetProperty("function");
                    var name = fn.GetProperty("name").GetString() ?? "";
                    var argsJson = fn.GetProperty("arguments").GetString() ?? "{}";
                    var id = "";
                    if (tc.TryGetProperty("id", out var idElement))
                        id = idElement.GetString() ?? "";
                    if (string.IsNullOrEmpty(id) || id == "call_0")
                        id = $"call_{Guid.NewGuid():N}";

                    // Parse args JSON into dictionary
                    var args = new Dictionary<string, string>();
                    try
                    {
                        using var argsDoc = JsonDocument.Parse(argsJson);
                        foreach (var prop in argsDoc.RootElement.EnumerateObject())
                        {
                            args[prop.Name] = prop.Value.ToString();
                        }
                    }
                    catch { /* leave args empty if JSON parse fails */ }

                    toolCalls.Add(new LlmToolCall(id, name, args));
                }
            }

            return new LlmResponse(content, toolCalls);
        }

        // Try simple content field
        if (root.TryGetProperty("content", out var simpleContent) && simpleContent.ValueKind != JsonValueKind.Null)
        {
            return new LlmResponse(simpleContent.GetString() ?? "", null);
        }

        // Try text field
        if (root.TryGetProperty("text", out var text) && text.ValueKind != JsonValueKind.Null)
        {
            return new LlmResponse(text.GetString() ?? "", null);
        }

        // Return raw JSON as content
        return new LlmResponse(root.GetRawText(), null);
    }

    private object MapMessage(LlmMessage message)
    {
        if (message.Role == "tool")
        {
            return new
            {
                role = "tool",
                tool_call_id = message.ToolCallId,
                name = message.ToolName,
                content = message.Content
            };
        }

        if (message.Role == "assistant" && message.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = string.IsNullOrEmpty(message.Content) ? null : message.Content,
                tool_calls = message.ToolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        arguments = JsonSerializer.Serialize(call.Arguments, JsonOptions)
                    }
                }).ToArray()
            };
        }

        return new
        {
            role = message.Role,
            content = message.Content
        };
    }

    private object[] BuildMessages(LlmRequest request)
    {
        var messages = new List<object>();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }
        foreach (var msg in request.Messages)
        {
            messages.Add(MapMessage(msg));
        }
        return messages.ToArray();
    }
}

public sealed record GenericHttpOptions(
    string Name,
    string Model,
    string ApiKey,
    string BaseUrl,
    string Endpoint,
    string AuthHeader);