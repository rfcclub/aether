namespace Aether.Agent;

public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct);
}

public sealed record ToolCall(string Name, IReadOnlyDictionary<string, string> Arguments);

public sealed record ToolResult(bool Succeeded, string Output, string? Error = null);
