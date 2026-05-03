using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Aether.Providers;

/// <summary>
/// Abstract base class for OpenAI-compatible providers.
/// Handles common HTTP patterns: auth, JSON serialization, error handling, response parsing.
/// </summary>
public abstract class OpenAiCompatibleProviderBase : ILLMProvider
{
    protected readonly HttpClient Client;
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public abstract string Name { get; }
    public abstract string Model { get; }
    public virtual bool SupportsStreaming => true;
    public virtual bool SupportsTools => true;

    protected abstract string GetEndpoint();
    protected abstract string GetApiKey();
    protected abstract string GetBaseUrl();

    protected OpenAiCompatibleProviderBase(HttpClient client)
    {
        Client = client;
    }

    /// <summary>Override to set Client.BaseAddress after construction.</summary>
    public virtual void Initialize() { }

    public virtual async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetEndpoint());
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());

        var body = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["messages"] = request.Messages.Select(MapMessage).ToArray()
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(MapTool).ToArray();
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

        var message = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var content = "";
        if (message.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind != JsonValueKind.Null)
        {
            content = contentElement.GetString() ?? "";
        }

        var toolCalls = ParseToolCalls(message);
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
    /// Event-based streaming that yields both text tokens and the final response
    /// (with optional tool calls). The default implementation falls back to
    /// non-streaming: it yields all content as a single TextToken followed by
    /// a Response event. Subclasses should override with real SSE parsing to
    /// yield individual tokens and properly accumulate tool call deltas.
    /// </summary>
    public virtual async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Check if we should use SSE streaming (tools or plain text)
        var useStreaming = request.Tools is { Count: > 0 } || SupportsStreaming;

        if (!useStreaming)
        {
            var nonStreamingResponse = await CompleteAsync(request, ct);
            yield return new StreamEvent.TextToken(nonStreamingResponse.Content);
            yield return new StreamEvent.Response(nonStreamingResponse);
            yield break;
        }

        // Build the streaming request
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, GetEndpoint());
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());

        var body = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["messages"] = request.Messages.Select(MapMessage).ToArray(),
            ["stream"] = true
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(MapTool).ToArray();
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

        // Accumulators for tool calls across the stream
        var fullContent = new StringBuilder();
        var toolCallAccumulators = new Dictionary<int, (string Id, string Name, StringBuilder Arguments)>();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (line.Length == 0) continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                // Comment lines (e.g., ": heartbeat") are ignored
                if (line.StartsWith(':')) continue;
                continue;
            }

            var data = line.AsSpan(6); // Skip "data: "

            if (data is " [DONE]" or "[DONE]")
            {
                // Stream complete
                break;
            }

            // Parse the JSON chunk (convert to string -- Span<char> overload requires byte sequence)
            using var chunk = JsonDocument.Parse(data.ToString());

            var root = chunk.RootElement;

            // Extract choices[0].delta
            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                continue;
            }

            var delta = choices[0].TryGetProperty("delta", out var d) ? d :
                        choices[0].TryGetProperty("message", out var m) ? m :
                        default;

            if (delta.ValueKind == JsonValueKind.Undefined) continue;

            // Text content delta
            if (delta.TryGetProperty("content", out var contentElement) &&
                contentElement.ValueKind != JsonValueKind.Null)
            {
                var token = contentElement.GetString() ?? "";
                if (token.Length > 0)
                {
                    fullContent.Append(token);
                    yield return new StreamEvent.TextToken(token);
                }
            }

            // Tool call deltas
            if (delta.TryGetProperty("tool_calls", out var toolCallsElement) &&
                toolCallsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCallsElement.EnumerateArray())
                {
                    var index = tc.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;

                    if (!toolCallAccumulators.TryGetValue(index, out var acc))
                    {
                        var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        var name = tc.TryGetProperty("function", out var fnEl) &&
                                    fnEl.TryGetProperty("name", out var nmEl)
                            ? nmEl.GetString() ?? ""
                            : "";
                        toolCallAccumulators[index] = (id, name, new StringBuilder());
                        acc = toolCallAccumulators[index];
                    }
                    else if (tc.TryGetProperty("function", out var fnEl) &&
                             fnEl.TryGetProperty("name", out var nmEl))
                    {
                        // Some SSE implementations send name later; update if present
                        var name = nmEl.GetString() ?? "";
                        if (name.Length > 0 && acc.Name.Length == 0)
                        {
                            acc = (acc.Id, name, acc.Arguments);
                        }
                    }

                    // Accumulate function arguments delta
                    if (tc.TryGetProperty("function", out var fnEl2) &&
                        fnEl2.TryGetProperty("arguments", out var argsEl) &&
                        argsEl.ValueKind != JsonValueKind.Null)
                    {
                        var argsStr = argsEl.GetString() ?? "";
                        if (argsStr.Length > 0)
                        {
                            acc.Arguments.Append(argsStr);
                        }
                    }

                    // Update the accumulator (struct semantics -- must re-assign)
                    toolCallAccumulators[index] = acc;
                }
            }
        }

        // Build final response with accumulated tool calls
        var toolCalls = new List<LlmToolCall>();
        foreach (var (_, (id, name, argsSb)) in toolCallAccumulators.OrderBy(kvp => kvp.Key))
        {
            var argsJson = argsSb.ToString();
            var arguments = string.IsNullOrEmpty(argsJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson, JsonOptions)
                  ?? new Dictionary<string, string>();

            toolCalls.Add(new LlmToolCall(id, name, arguments));
        }

        var finalResponse = new LlmResponse(
            fullContent.ToString(),
            toolCalls.Count > 0 ? toolCalls : null);

        yield return new StreamEvent.Response(finalResponse);
    }

    public virtual async Task<bool> HealthCheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());

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

    protected virtual object MapTool(LlmTool tool)
    {
        using var parameters = JsonDocument.Parse(tool.ParametersJson);
        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = parameters.RootElement.Clone()
            }
        };
    }

    protected static IReadOnlyList<LlmToolCall> ParseToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var callsElement)
            || callsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LlmToolCall>();
        }

        var calls = new List<LlmToolCall>();
        foreach (var callElement in callsElement.EnumerateArray())
        {
            var id = callElement.GetProperty("id").GetString() ?? "";
            var function = callElement.GetProperty("function");
            var name = function.GetProperty("name").GetString() ?? "";
            var argumentsJson = function.GetProperty("arguments").GetString() ?? "{}";
            var arguments = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson, JsonOptions)
                ?? new Dictionary<string, string>();

            calls.Add(new LlmToolCall(id, name, arguments));
        }

        return calls;
    }
}