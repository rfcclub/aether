using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Aether.Providers;

/// <summary>
/// Abstract base class for Anthropic-compatible providers.
/// Handles Anthropic-specific headers, message format, tool use conversion.
/// </summary>
public abstract class AnthropicCompatibleProviderBase : ILLMProvider
{
    protected readonly HttpClient Client;
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string DefaultAnthropicVersion = "2023-06-01";

    public abstract string Name { get; }
    public abstract string Model { get; }
    public virtual bool SupportsStreaming => true;
    public virtual bool SupportsTools => true;

    protected abstract string GetApiKey();
    protected abstract string GetBaseUrl();
    protected virtual string GetAnthropicVersion() => DefaultAnthropicVersion;
    protected virtual string GetEndpoint() => "messages";
    protected virtual int GetMaxTokens() => 4096;

    protected AnthropicCompatibleProviderBase(HttpClient client)
    {
        Client = client;
        if (Client.BaseAddress is null)
        {
            Client.BaseAddress = new Uri(GetBaseUrl().TrimEnd('/') + "/");
        }
    }

    public virtual async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetEndpoint());
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());
        httpRequest.Headers.Add("x-api-key", GetApiKey());
        httpRequest.Headers.Add("anthropic-version", GetAnthropicVersion());

        var body = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["messages"] = request.Messages.Select(MapMessage).ToArray(),
            ["max_tokens"] = GetMaxTokens()
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(MapTool).ToArray();
            body["tool_choice"] = new { type = "auto" };
        }

        httpRequest.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await Client.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"{Name} request failed with HTTP {(int)response.StatusCode}. Response: {errorBody}");
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
            _ = usage; // Token usage available for cost tracking
        }

        return new LlmResponse(content, toolCalls);
    }

    public virtual async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Fallback: non-streaming. Subclasses override with real SSE streaming.
        var response = await CompleteAsync(request, ct);
        yield return response.Content;
    }

    /// <summary>
    /// Event-based streaming that yields text tokens and a final Response event
    /// (with accumulated tool calls). Parses Anthropic's SSE stream format
    /// (event: content_block_start / content_block_delta / content_block_stop / message_stop).
    /// </summary>
    public virtual async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetEndpoint());
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());
        httpRequest.Headers.Add("x-api-key", GetApiKey());
        httpRequest.Headers.Add("anthropic-version", GetAnthropicVersion());

        var body = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["messages"] = request.Messages.Select(MapMessage).ToArray(),
            ["max_tokens"] = GetMaxTokens(),
            ["stream"] = true
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(MapTool).ToArray();
            body["tool_choice"] = new { type = "auto" };
        }

        httpRequest.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"{Name} SSE request failed with HTTP {(int)response.StatusCode}. Response: {errorBody}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);

        var fullContent = new StringBuilder();
        var pendingToolCallId = "";
        var pendingToolCallName = "";
        var pendingToolInputBuilder = new StringBuilder();
        var toolCalls = new List<LlmToolCall>();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (line.Length == 0) continue;

            // Anthropic SSE: "event: <type>" then "data: <json>"
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line.AsSpan(6);
            var dataStr = data.ToString();

            using var chunk = JsonDocument.Parse(dataStr);
            var root = chunk.RootElement;

            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "";

            switch (type)
            {
                case "content_block_delta":
                {
                    var delta = root.GetProperty("delta");
                    var deltaType = delta.TryGetProperty("type", out var dtEl) ? dtEl.GetString() : "";

                    if (deltaType == "text_delta")
                    {
                        var token = delta.GetProperty("text").GetString() ?? "";
                        if (token.Length > 0)
                        {
                            fullContent.Append(token);
                            yield return new StreamEvent.TextToken(token);
                        }
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var partialJson = delta.GetProperty("partial_json").GetString() ?? "";
                        pendingToolInputBuilder.Append(partialJson);
                    }
                    break;
                }

                case "content_block_start":
                {
                    var block = root.GetProperty("content_block");
                    var blockType = block.TryGetProperty("type", out var btEl) ? btEl.GetString() : "";

                    if (blockType == "tool_use")
                    {
                        pendingToolCallId = block.GetProperty("id").GetString() ?? "";
                        pendingToolCallName = block.GetProperty("name").GetString() ?? "";
                        pendingToolInputBuilder.Clear();
                    }
                    else if (blockType == "text")
                    {
                        var text = block.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                        if (text.Length > 0)
                        {
                            fullContent.Append(text);
                            yield return new StreamEvent.TextToken(text);
                        }
                    }
                    break;
                }

                case "content_block_stop":
                {
                    if (pendingToolCallId.Length > 0)
                    {
                        var argsJson = pendingToolInputBuilder.ToString();
                        var arguments = string.IsNullOrEmpty(argsJson)
                            ? new Dictionary<string, string>()
                            : JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson, JsonOptions)
                              ?? new Dictionary<string, string>();

                        toolCalls.Add(new LlmToolCall(pendingToolCallId, pendingToolCallName, arguments));

                        pendingToolCallId = "";
                        pendingToolCallName = "";
                        pendingToolInputBuilder.Clear();
                    }
                    break;
                }

                case "message_stop":
                {
                    var finalResponse = new LlmResponse(
                        fullContent.ToString(),
                        toolCalls.Count > 0 ? toolCalls : null);
                    yield return new StreamEvent.Response(finalResponse);
                    yield break;
                }

                case "message_start":
                case "ping":
                case "error":
                default:
                    break;
            }
        }

        // Stream ended without message_stop -- emit what we have
        if (pendingToolCallId.Length > 0)
        {
            var argsJson = pendingToolInputBuilder.ToString();
            var arguments = string.IsNullOrEmpty(argsJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson, JsonOptions)
                  ?? new Dictionary<string, string>();
            toolCalls.Add(new LlmToolCall(pendingToolCallId, pendingToolCallName, arguments));
        }

        var fallbackResponse = new LlmResponse(
            fullContent.ToString(),
            toolCalls.Count > 0 ? toolCalls : null);
        yield return new StreamEvent.Response(fallbackResponse);
    }

    public virtual async Task<bool> HealthCheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "messages");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());
            request.Headers.Add("anthropic-version", GetAnthropicVersion());

            var body = new
            {
                model = Model,
                messages = new[] { new { role = "user", content = "ping" } },
                max_tokens = 1
            };

            request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await Client.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    protected virtual object MapMessage(LlmMessage message)
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

    protected virtual object MapTool(LlmTool tool)
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