using System.CommandLine;
using System.Text.Json;
using Aether.Config;
using Aether.Plugins;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Aether.Cli;

public sealed class PluginCli
{
    private readonly string _aetherDir;
    private readonly ILogger<PluginCli> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public PluginCli(string aetherDir, ILogger<PluginCli> logger)
    {
        _aetherDir = aetherDir;
        _logger = logger;
    }

    public Command BuildPluginCommand()
    {
        var plugin = new Command("plugin", "Manage Aether plugins");

        plugin.AddCommand(BuildListCommand());
        plugin.AddCommand(BuildInstallCommand());
        plugin.AddCommand(BuildShowCommand());
        plugin.AddCommand(BuildEnableCommand());
        plugin.AddCommand(BuildDisableCommand());
        plugin.AddCommand(BuildUninstallCommand());

        return plugin;
    }

    // ── plugin list ──

    private Command BuildListCommand()
    {
        var cmd = new Command("list", "List installed plugins");

        cmd.SetHandler(async (context) =>
        {
            var pluginsDir = Path.Combine(_aetherDir, "plugins");
            if (!Directory.Exists(pluginsDir) || Directory.GetDirectories(pluginsDir).Length == 0)
            {
                AnsiConsole.MarkupLine("[dim]No plugins installed.[/]");
                return;
            }

            var dirs = Directory.GetDirectories(pluginsDir);
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Name[/]"))
                .AddColumn(new TableColumn("[bold]Version[/]"))
                .AddColumn(new TableColumn("[bold]Status[/]"));

            foreach (var dir in dirs)
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath, context.GetCancellationToken());
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? Path.GetFileName(dir) : Path.GetFileName(dir);
                    var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "-" : "-";

                    table.AddRow(
                        $"[violet]{Markup.Escape(name)}[/]",
                        $"[dim]{Markup.Escape(version)}[/]",
                        "[green]installed[/]");
                }
                catch
                {
                    table.AddRow(
                        $"[violet]{Markup.Escape(Path.GetFileName(dir))}[/]",
                        "[dim]?[/]",
                        "[red]error[/]");
                }
            }

            AnsiConsole.Write(table);
        });

        return cmd;
    }

    // ── plugin install ──

    private Command BuildInstallCommand()
    {
        var pathArg = new Argument<string>("path", "Path to plugin directory");
        var forceOpt = new Option<bool>("--force", "Overwrite if already installed");

        var cmd = new Command("install", "Install a plugin") { pathArg, forceOpt };

        cmd.SetHandler(async (context) =>
        {
            var sourcePath = context.ParseResult.GetValueForArgument(pathArg);
            var force = context.ParseResult.GetValueForOption(forceOpt);
            var ct = context.GetCancellationToken();

            sourcePath = Path.GetFullPath(sourcePath);

            if (!Directory.Exists(sourcePath))
            {
                context.Console.WriteLine($"Error: Directory not found: {sourcePath}");
                context.ExitCode = 1;
                return;
            }

            var manifestPath = Path.Combine(sourcePath, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                context.Console.WriteLine($"Error: plugin.json not found in {sourcePath}");
                context.ExitCode = 1;
                return;
            }

            string pluginName;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, ct);
                using var doc = JsonDocument.Parse(json);
                pluginName = doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()!
                    : throw new InvalidOperationException("plugin.json missing required 'name' field");
            }
            catch (Exception ex)
            {
                context.Console.WriteLine($"Error: Invalid plugin.json — {ex.Message}");
                context.ExitCode = 1;
                return;
            }

            var pluginsDir = Path.Combine(_aetherDir, "plugins");
            Directory.CreateDirectory(pluginsDir);
            var destPath = Path.Combine(pluginsDir, pluginName);

            if (Directory.Exists(destPath) && !force)
            {
                context.Console.WriteLine($"Plugin '{pluginName}' already installed. Use --force to overwrite.");
                context.ExitCode = 1;
                return;
            }

            if (Directory.Exists(destPath))
            {
                Directory.Delete(destPath, recursive: true);
            }

            CopyDirectory(sourcePath, destPath);
            context.Console.WriteLine($"Plugin '{pluginName}' installed.");
        });

        return cmd;
    }

    // ── plugin show ──

    private Command BuildShowCommand()
    {
        var nameArg = new Argument<string>("name", "Plugin name");

        var cmd = new Command("show", "Show plugin details") { nameArg };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var pluginsDir = Path.Combine(_aetherDir, "plugins");
            var pluginDir = Path.Combine(pluginsDir, name);
            var manifestPath = Path.Combine(pluginDir, "plugin.json");

            if (!File.Exists(manifestPath))
            {
                AnsiConsole.MarkupLine($"[red]Plugin '{Markup.Escape(name)}' not found.[/]");
                context.ExitCode = 1;
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, context.GetCancellationToken());
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                AnsiConsole.Write(new Rule($"[bold violet]Plugin: {Markup.Escape(name)}[/]")
                    .RuleStyle("grey"));

                var grid = new Grid().AddColumn().AddColumn();
                grid.AddRow("[dim]Name[/]", GetField(root, "name"));
                grid.AddRow("[dim]Version[/]", GetField(root, "version"));
                grid.AddRow("[dim]Display name[/]", GetField(root, "displayName"));
                grid.AddRow("[dim]Description[/]", GetField(root, "description"));
                grid.AddRow("[dim]Author[/]", GetField(root, "author"));
                grid.AddRow("[dim]License[/]", GetField(root, "license"));
                grid.AddRow("[dim]Homepage[/]", GetField(root, "homepage"));
                grid.AddRow("[dim]Assembly[/]", GetField(root, "assembly"));

                var hooks = GetList(root, "hooks", "class");
                var tools = GetList(root, "tools", "name");
                var skills = GetList(root, "skills", "name");
                var channels = GetList(root, "channels", "class");
                var cron = GetList(root, "cron", "name");

                if (hooks != null) grid.AddRow("[dim]Hooks[/]", hooks);
                if (tools != null) grid.AddRow("[dim]Tools[/]", tools);
                if (skills != null) grid.AddRow("[dim]Skills[/]", skills);
                if (channels != null) grid.AddRow("[dim]Channels[/]", channels);
                if (cron != null) grid.AddRow("[dim]Cron tasks[/]", cron);

                AnsiConsole.Write(grid);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error reading plugin: {Markup.Escape(ex.Message)}[/]");
                context.ExitCode = 1;
            }
        });

        return cmd;
    }

    // ── plugin enable ──

    private Command BuildEnableCommand()
    {
        var nameArg = new Argument<string>("name", "Plugin name");
        var agentOpt = new Option<string>("--agent", "Target agent") { IsRequired = true };

        var cmd = new Command("enable", "Enable a plugin for an agent") { nameArg, agentOpt };

        cmd.SetHandler(async (context) =>
        {
            var pluginName = context.ParseResult.GetValueForArgument(nameArg);
            var agentName = context.ParseResult.GetValueForOption(agentOpt)!;
            var ct = context.GetCancellationToken();

            await TogglePluginAsync(context.Console, pluginName, agentName, enabled: true, ct);
        });

        return cmd;
    }

    // ── plugin disable ──

    private Command BuildDisableCommand()
    {
        var nameArg = new Argument<string>("name", "Plugin name");
        var agentOpt = new Option<string>("--agent", "Target agent") { IsRequired = true };

        var cmd = new Command("disable", "Disable a plugin for an agent") { nameArg, agentOpt };

        cmd.SetHandler(async (context) =>
        {
            var pluginName = context.ParseResult.GetValueForArgument(nameArg);
            var agentName = context.ParseResult.GetValueForOption(agentOpt)!;
            var ct = context.GetCancellationToken();

            await TogglePluginAsync(context.Console, pluginName, agentName, enabled: false, ct);
        });

        return cmd;
    }

    // ── plugin uninstall ──

    private Command BuildUninstallCommand()
    {
        var nameArg = new Argument<string>("name", "Plugin name");
        var forceOpt = new Option<bool>("--force", "Remove without confirmation");

        var cmd = new Command("uninstall", "Uninstall a plugin") { nameArg, forceOpt };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var force = context.ParseResult.GetValueForOption(forceOpt);
            var pluginsDir = Path.Combine(_aetherDir, "plugins");
            var pluginDir = Path.Combine(pluginsDir, name);

            if (!Directory.Exists(pluginDir))
            {
                context.Console.WriteLine($"Plugin '{name}' not found.");
                context.ExitCode = 1;
                return;
            }

            var manifestPath = Path.Combine(pluginDir, "plugin.json");
            int fileCount = 0;
            string version = "?";
            if (File.Exists(manifestPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, context.GetCancellationToken()));
                    version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "?" : "?";
                }
                catch { }
            }
            fileCount = Directory.GetFiles(pluginDir, "*", SearchOption.AllDirectories).Length;

            if (!force)
            {
                context.Console.WriteLine($"Plugin: {name} v{version} ({fileCount} files)");
                context.Console.Write("Uninstall? [y/N] ");
                var answer = Console.ReadLine();
                if (string.IsNullOrEmpty(answer) || !answer.StartsWith('y'))
                {
                    context.Console.WriteLine("Cancelled.");
                    return;
                }
            }

            Directory.Delete(pluginDir, recursive: true);
            context.Console.WriteLine($"Plugin '{name}' uninstalled.");
        });

        return cmd;
    }

    // ── Helpers ──

    private async Task TogglePluginAsync(IConsole console, string pluginName, string agentName, bool enabled, CancellationToken ct)
    {
        // Verify plugin exists
        var pluginsDir = Path.Combine(_aetherDir, "plugins");
        var manifestPath = Path.Combine(pluginsDir, pluginName, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            console.WriteLine($"Plugin '{pluginName}' not found.");
            return;
        }

        // Find agent workspace from ~/.aether/config.json
        var configPath = Path.Combine(_aetherDir, "config.json");
        string? workspacePath = null;
        if (File.Exists(configPath))
        {
            var configJson = await File.ReadAllTextAsync(configPath, ct);
            using var doc = JsonDocument.Parse(configJson);
            if (doc.RootElement.TryGetProperty("agents", out var agents))
            {
                foreach (var prop in agents.EnumerateObject())
                {
                    if (string.Equals(prop.Name, agentName, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.TryGetProperty("workspace", out var ws))
                    {
                        workspacePath = ws.GetString();
                        break;
                    }
                }
            }
        }

        if (workspacePath is null)
        {
            console.WriteLine($"Agent '{agentName}' not found.");
            return;
        }
        var aetherJsonPath = Path.Combine(workspacePath, ".aether.json");

        AgentSpecConfig spec;
        if (File.Exists(aetherJsonPath))
        {
            var json = await File.ReadAllTextAsync(aetherJsonPath, ct);
            spec = JsonSerializer.Deserialize<AgentSpecConfig>(json, JsonOptions) ?? new AgentSpecConfig();
        }
        else
        {
            spec = new AgentSpecConfig();
        }

        spec.Plugins ??= new AgentPluginConfig();

        // Remove from both lists first
        spec.Plugins.Enabled.RemoveAll(p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase));
        spec.Plugins.Disabled.RemoveAll(p => string.Equals(p, pluginName, StringComparison.OrdinalIgnoreCase));

        // Add to appropriate list
        if (enabled)
            spec.Plugins.Enabled.Add(pluginName);
        else
            spec.Plugins.Disabled.Add(pluginName);

        var outputJson = JsonSerializer.Serialize(spec, JsonOptions);
        await File.WriteAllTextAsync(aetherJsonPath, outputJson, ct);

        console.WriteLine($"Plugin '{pluginName}' {(enabled ? "enabled" : "disabled")} for agent '{agentName}'.");
    }

    private static string GetField(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return Markup.Escape(prop.GetString() ?? "");
        }
        return "[dim]-[/]";
    }

    private static string? GetList(JsonElement root, string arrayKey, string itemKey)
    {
        if (!root.TryGetProperty(arrayKey, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var items = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(itemKey, out var val))
            {
                items.Add(Markup.Escape(val.GetString() ?? ""));
            }
        }
        return items.Count == 0 ? null : string.Join(", ", items);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
