using Aether.Plugins;
using Aether.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins.MariaMemory.Hooks;

public sealed class MariaCommandHook : IHook
{
    public string Name => "MariaCommands";
    public HookPoint SubscribesTo => HookPoint.OnMessageRouted | HookPoint.OnMessageReceived;
    public int Priority => 10;

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
    {
        string text;
        string workspacePath = context.WorkspacePath;
        string chatId;

        if (context is OnMessageRoutedContext routed)
        {
            text = routed.Message.Text.Trim();
            chatId = routed.Message.ChatId;
        }
        else if (context is OnMessageReceivedContext received)
        {
            text = received.Text.Trim();
            chatId = received.ChatId;
        }
        else return HookResult.Continue;

        if (!text.StartsWith("/") || (!text.StartsWith("/memory") && !text.StartsWith("/2b")))
            return HookResult.Continue;

        var spaceIdx = text.IndexOf(' ');
        var command = (spaceIdx < 0 ? text : text[..spaceIdx]).ToLowerInvariant();
        var args = spaceIdx < 0 ? "" : text[(spaceIdx + 1)..].Trim();

        string response;
        switch (command)
        {
            case "/memory":
                response = await HandleMemoryCommandAsync(args, workspacePath, ct);
                break;
            case "/2b":
                response = await Handle2bCommandAsync(args, workspacePath, context, ct);
                break;
            default:
                return HookResult.Continue;
        }

        var channel = MariaMemoryLifecycle.Services?.GetService<IChannel>();
        if (channel != null)
        {
            await channel.SendMessageAsync(chatId, response, ct);
        }
        else
        {
            Console.WriteLine(response);
        }
        
        return HookResult.Stop("Handled by MariaMemoryPlugin");
    }

    private async Task<string> HandleMemoryCommandAsync(string args, string workspacePath, CancellationToken ct)
    {
        var parts = args.Split(' ', 2);
        var subCommand = parts[0].ToLowerInvariant();

        switch (subCommand)
        {
            case "today":
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var path = Path.Combine(workspacePath, "memory", $"{today}.md");
                return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : "Today's memory file not found.";

            case "yesterday":
                var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
                var yPath = Path.Combine(workspacePath, "memory", $"{yesterday}.md");
                return File.Exists(yPath) ? await File.ReadAllTextAsync(yPath, ct) : "Yesterday's memory file not found.";

            case "search":
                if (parts.Length < 2) return "Usage: /memory search <query>";
                var searchStore = MariaMemoryLifecycle.Store ?? new MariaMemoryStore(workspacePath, NullLogger.Instance);
                var results = await searchStore.SearchAsync(parts[1], 5, ct);
                if (results.Count == 0) return "No matches found.";
                return "Matches found:\n" + string.Join("\n\n", results.Select(r => $"[{r.Timestamp:yyyy-MM-dd HH:mm}] {r.Content}"));

            case "link-research":
                if (parts.Length < 2) return "Usage: /memory link-research <topic>";
                var linker = MariaMemoryLifecycle.Linker ?? new ResearchLinker(workspacePath, new MariaSqliteStore(workspacePath, NullLogger.Instance), NullLogger.Instance);
                await linker.CreateLinkAsync("current_session", parts[1], ct);
                return $"Memory linked to research topic: {parts[1]}";

            case "promote":
                if (MariaMemoryLifecycle.Dreamer != null)
                {
                    await MariaMemoryLifecycle.Dreamer.PerformDreamCycleAsync(ct);
                    return "Auto-promotion and dreaming cycle triggered.";
                }
                return "Dreamer not initialized.";

            default:
                return "Usage: /memory [today|yesterday|search|link-research|promote]";
        }
    }

    private async Task<string> Handle2bCommandAsync(string args, string workspacePath, HookContext context, CancellationToken ct)
    {
        var parts = args.Split(' ', 2);
        var subCommand = parts[0].ToLowerInvariant();

        switch (subCommand)
        {
            case "end":
                context.Bag["DetectedTension"] = 1;
                context.Bag["TensionTrigger"] = "manual_ritual";
                return "2B Session-end ritual triggered. Tension mark recorded.";

            case "status":
                var tensionFile = Path.Combine(workspacePath, "2B", "TENSION_MARKS.md");
                var tension = File.Exists(tensionFile) ? await File.ReadAllTextAsync(tensionFile, ct) : "No tension marks recorded.";
                return $"Boundary Status:\n\n{tension}";

            default:
                return "Usage: /2b [end|status]";
        }
    }
}
