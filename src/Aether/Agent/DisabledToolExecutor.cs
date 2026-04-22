namespace Aether.Agent;

public sealed class DisabledToolExecutor : IToolExecutor
{
    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        return Task.FromResult(new ToolResult(false, "", "Tool execution is not enabled in this Track B foundation slice."));
    }
}
