namespace Aether.Plugins;

public interface IHook
{
    string Name { get; }
    HookPoint SubscribesTo { get; }
    int Priority { get; }
    Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct);
}
