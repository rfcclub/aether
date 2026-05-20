using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class ContextAssemblyHook : IHook
{
    public string Name => "MariaContextAssembly";
    public HookPoint SubscribesTo => HookPoint.PreLlmCall;
    public int Priority => 50;

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        if (context is not PreLlmCallContext preLlm)
            return HookResult.Continue;

        if (MariaMemoryLifecycle.ContextEngine == null)
            return HookResult.Continue;

        // Determine topic from the last message or current context
        var lastMsg = preLlm.Messages.LastOrDefault()?.Content ?? "";
        var smartContext = await MariaMemoryLifecycle.ContextEngine.AssembleContextAsync(lastMsg, 7000, ct);

        // Inject into system prompt
        preLlm.SystemPrompt = $"{preLlm.SystemPrompt}\n\n{smartContext}";

        return HookResult.Continue;
    }
}
