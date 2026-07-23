namespace Aether.Providers;

public interface ILLMProvider
{
    string Name { get; }
    string Model { get; }
    bool SupportsStreaming { get; }
    bool SupportsTools { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> CompleteStreamingAsync(LlmRequest request, CancellationToken ct = default);
    IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(LlmRequest request, CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}

public sealed record LlmRequest(
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool>? Tools = null,
    string? ReasoningEffort = null,
    int? ThinkingBudgetTokens = null,
    string? SystemPrompt = null,
    bool UsePromptCaching = false);

public sealed record LlmResponse(
    string Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    string? Reasoning = null);

public sealed record LlmTool(
    string Name,
    string Description,
    string ParametersJson,
    string? SchemaJson = null);

public sealed record LlmToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// Discriminated union for streaming events from the LLM.
/// Either a text token or a completed response (with optional tool calls).
/// The provider signals stream completion by yielding a Response event.
/// </summary>
public abstract record StreamEvent
{
    private StreamEvent() { }

    /// <summary>
    /// A single text token yielded during streaming.
    /// </summary>
    public sealed record TextToken(string Token) : StreamEvent;

    /// <summary>
    /// The complete response, yielded after all text tokens have been streamed.
    /// May include tool calls that were accumulated during streaming.
    /// </summary>
    public sealed record Response(LlmResponse LlmResponse) : StreamEvent;
}

public sealed class LlmMessage
{
    public string Role { get; }
    public string Content { get; }
    public string? ToolCallId { get; }
    public string? ToolName { get; }
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; }

    public LlmMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    private LlmMessage(string role, string content, string? toolCallId, string? toolName, IReadOnlyList<LlmToolCall>? toolCalls)
    {
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
        ToolName = toolName;
        ToolCalls = toolCalls;
    }

    public static LlmMessage System(string content) => new("system", content);
    public static LlmMessage User(string content) => new("user", content);

    public static LlmMessage AssistantToolCalls(string content, IReadOnlyList<LlmToolCall> toolCalls) =>
        new("assistant", content, null, null, toolCalls);

    public static LlmMessage ToolResult(string toolCallId, string toolName, string content) =>
        new("tool", content, toolCallId, toolName, null);
}
