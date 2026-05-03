using Aether.Config;

namespace Aether.Agent;

public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct);

    /// <summary>
    /// Set per-agent context before processing a turn.
    /// Call this before any ExecuteAsync to configure sandbox paths for the current agent.
    /// </summary>
    void SetAgentContext(string workspace, SpecToolsSection? toolsConfig = null) { }
}

public sealed record ToolCall(string Name, IReadOnlyDictionary<string, string> Arguments);

public sealed record ToolResult(bool Succeeded, string Output, string? Error = null);
