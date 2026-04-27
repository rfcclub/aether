using System.Net.Http.Headers;
using System.Net.Http.Json;
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