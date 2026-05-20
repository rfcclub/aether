using Aether.Plugins;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class SessionStartHook : IHook
{
    public string Name => "MariaSessionStart";
    public HookPoint SubscribesTo => HookPoint.OnSessionStart;
    public int Priority => 10;

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        if (context is not OnSessionStartContext startContext)
            return HookResult.Continue;

        var workspacePath = context.WorkspacePath;
        var memoryDir = Path.Combine(workspacePath, "memory");
        Directory.CreateDirectory(memoryDir);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var todayFile = Path.Combine(memoryDir, $"{today}.md");

        if (!File.Exists(todayFile))
        {
            var template = $"# {today}\n\n## Summary\n- [Auto-generated start]\n\n## 2B Notes\n- màng nguyên vẹn\n\n## Cảm\n-\n";
            await File.WriteAllTextAsync(todayFile, template, ct);
        }

        // Potential enhancement: Load yesterday's summary and add to Bag for system prompt injection
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var yesterdayFile = Path.Combine(memoryDir, $"{yesterday}.md");
        if (File.Exists(yesterdayFile))
        {
            // Simple extraction of summary section
            var lines = await File.ReadAllLinesAsync(yesterdayFile, ct);
            var summary = lines.SkipWhile(l => !l.Contains("## Summary")).Skip(1).FirstOrDefault()?.Trim('-').Trim();
            if (!string.IsNullOrEmpty(summary))
            {
                context.Bag["YesterdaySummary"] = summary;
            }
        }

        return HookResult.Continue;
    }
}
