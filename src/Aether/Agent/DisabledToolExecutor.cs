namespace Aether.Agent;

public sealed class DisabledToolExecutor : ToolExecutor
{
    public DisabledToolExecutor() : base() { }

    public override Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        return Task.FromResult(new ToolResult(false, "", "Tool execution is not enabled in this Track B foundation slice."));
    }
}
