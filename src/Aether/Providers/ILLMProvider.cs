namespace Aether.Providers;

public interface ILLMProvider
{
    string Name { get; }
    string Model { get; }
    bool SupportsStreaming { get; }
    bool SupportsTools { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}

public sealed record LlmRequest(
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool>? Tools = null);

public sealed record LlmResponse(
    string Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null);

public sealed record LlmTool(
    string Name,
    string Description,
    string ParametersJson,
    string? SchemaJson = null);

public sealed record LlmToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

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
