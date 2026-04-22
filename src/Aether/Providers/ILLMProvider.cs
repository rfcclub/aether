namespace Aether.Providers;

public interface ILLMProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool>? Tools = null);

public sealed record LlmMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    string? ToolName = null)
{
    public static LlmMessage System(string content) => new("system", content);

    public static LlmMessage User(string content) => new("user", content);

    public static LlmMessage Assistant(string content) => new("assistant", content);

    public static LlmMessage ToolResult(string toolCallId, string toolName, string content)
    {
        return new LlmMessage("tool", content, toolCallId, toolName);
    }
}

public sealed record LlmTool(
    string Name,
    string Description,
    string ParametersJson);

public sealed record LlmResponse(
    string Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null);

public sealed record LlmToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);
