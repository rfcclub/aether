using Aether.Plugins;
using Aether.Plugins.MariaMemory.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class AutoSaveHook : IHook
{
    public string Name => "MariaAutoSave";
    public HookPoint SubscribesTo => HookPoint.OnMessageReceived | HookPoint.OnMessageSent;
    public int Priority => 200; // Low priority, let others redact first

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.WorkspacePath)) return HookResult.Continue;

        string? content = null;
        string role = "";

        if (context is OnMessageReceivedContext received)
        {
            content = received.Text;
            role = "user";
        }
        else if (context is OnMessageSentContext sent)
        {
            content = sent.OverrideText ?? sent.Text;
            role = "assistant";
        }

        if (string.IsNullOrWhiteSpace(content)) return HookResult.Continue;

        var store = new MariaMemoryStore(context.WorkspacePath, NullLogger.Instance);
        var node = new MemoryNode
        {
            Content = content,
            Role = role,
            Source = context is OnMessageReceivedContext r ? r.ChannelName : "aether"
        };

        await store.AppendAsync(node, ct);

        return HookResult.Continue;
    }
}
