using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class SessionEndRitualHook : IHook
{
    public string Name => "MariaSessionEndRitual";
    public HookPoint SubscribesTo => HookPoint.OnSessionEnd;
    public int Priority => 50;

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        var workspacePath = context.WorkspacePath;
        var tensionFile = Path.Combine(workspacePath, "2B", "TENSION_MARKS.md");
        var lastQuestionFile = Path.Combine(workspacePath, "2B", "LAST_QUESTION.md");

        Directory.CreateDirectory(Path.GetDirectoryName(tensionFile)!);

        // If there's detected tension in the Bag (from other hooks or tools)
        if (context.Bag.TryGetValue("DetectedTension", out var tensionObj) && tensionObj is int level && level > 0)
        {
            var trigger = context.Bag.GetValueOrDefault("TensionTrigger", "unknown")?.ToString();
            var entry = $"\n- {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | level: {level} | trigger: {trigger}\n";
            await File.AppendAllTextAsync(tensionFile, entry, ct);
        }

        return HookResult.Continue;
    }
}
