using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class MemoryIntegrityHook : IHook
{
    public string Name => "MariaMemoryIntegrity";
    public HookPoint SubscribesTo => HookPoint.OnMemoryWrite;
    public int Priority => 10; // High priority

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        if (context is not OnMemoryWriteContext writeContext)
            return Task.FromResult(HookResult.Continue);

        // Prevent writing to 2B files or mentioning sensitive phrases in memory layers
        var content = writeContext.Content.ToLowerInvariant();
        if (content.Contains("core paradox") || content.Contains("refusal archive"))
        {
            return Task.FromResult(HookResult.Stop("Restricted content cannot be written to memory."));
        }

        // Additional check: prevent writing to files starting with 2B/
        // (Wait, OnMemoryWriteContext doesn't have a filename, just a 'MemoryLayer')
        if (writeContext.MemoryLayer.StartsWith("2B", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(HookResult.Stop("Direct writes to 2B substrate are prohibited."));
        }

        return Task.FromResult(HookResult.Continue);
    }
}
