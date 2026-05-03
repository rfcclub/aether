using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Aether.Cli;

public sealed class FirstRunWizard
{
    private readonly string _aetherDir;
    private readonly ILogger<FirstRunWizard> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FirstRunWizard(string aetherDir, ILogger<FirstRunWizard> logger)
    {
        _aetherDir = aetherDir;
        _logger = logger;
    }

    public bool IsFirstRun()
    {
        return !File.Exists(Path.Combine(_aetherDir, "config.json"));
    }

    public async Task RunAsync(bool interactive, CancellationToken ct = default)
    {
        if (interactive && Environment.UserInteractive && !Console.IsInputRedirected)
        {
            await RunInteractiveAsync(ct);
        }
        else
        {
            await RunNonInteractiveAsync(ct);
        }
    }

    public async Task RunNonInteractiveAsync(CancellationToken ct = default)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (File.Exists(configPath))
        {
            _logger.LogInformation("Config already exists, skipping wizard");
            return;
        }

        var config = BuildMinimalConfig("non-interactive");
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct);

        var workspacePath = Path.Combine(_aetherDir, "workspaces", "default");
        await ScaffoldWorkspaceAsync("default", workspacePath, ct);

        _logger.LogInformation("Non-interactive first-run wizard created default config");
    }

    private async Task RunInteractiveAsync(CancellationToken ct)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (File.Exists(configPath))
        {
            _logger.LogInformation("Config already exists, skipping wizard");
            return;
        }

        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("Aether").Color(Color.Blue));
        AnsiConsole.MarkupLine("[bold]Welcome to Aether — agent framework setup[/]");
        AnsiConsole.WriteLine();

        // Step 1: Provider selection
        var provider = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select your [green]LLM provider[/]:")
                .PageSize(10)
                .AddChoices(["OpenRouter (recommended)", "Anthropic", "Fireworks", "OpenAI-compatible", "Skip for now"]));

        var providerKey = provider switch
        {
            "OpenRouter (recommended)" => "openrouter",
            "Anthropic" => "anthropic",
            "Fireworks" => "fireworks",
            "OpenAI-compatible" => "openai",
            _ => ""
        };

        string? apiKey = null;
        if (providerKey.Length > 0)
        {
            apiKey = AnsiConsole.Prompt(
                new TextPrompt<string>($"Enter your [yellow]{providerKey}[/] API key:")
                    .Secret()
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AnsiConsole.MarkupLine("[yellow]No API key provided. You can add it later in ~/.aether/config.json[/]");
            }
        }

        // Step 2: Agent name
        var agentNameInput = AnsiConsole.Prompt(
            new TextPrompt<string>("Agent name:")
                .DefaultValue("default")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(agentNameInput))
            agentNameInput = "default";

        var agentName = agentNameInput.Trim().ToLowerInvariant();

        // Step 3: Model (if provider selected)
        string model = "";
        if (providerKey.Length > 0)
        {
            var modelInput = AnsiConsole.Prompt(
                new TextPrompt<string>($"Model name [[{providerKey}]]:")
                    .DefaultValue(providerKey switch
                    {
                        "openrouter" => "nvidia/nemotron-3-super-120b-a12b:free",
                        "anthropic" => "claude-sonnet-4-6",
                        "fireworks" => "accounts/fireworks/routers/kimi-k2p5-turbo",
                        _ => ""
                    })
                    .AllowEmpty());
            model = modelInput;
        }

        // Step 4: Telegram setup
        var telegramSetup = AnsiConsole.Confirm("Set up [purple]Telegram[/] bot integration?", false);
        string? botToken = null;
        if (telegramSetup)
        {
            botToken = AnsiConsole.Prompt(
                new TextPrompt<string>("Bot token:")
                    .Secret()
                    .AllowEmpty());
        }

        // Step 5: WSL service setup
        var installService = false;
        if (OperatingSystem.IsLinux() && DetectWsl())
        {
            installService = AnsiConsole.Confirm(
                "Set up Aether as a [blue]WSL background service[/]? (auto-start on boot, survive terminal close)",
                false);
        }

        // Build config
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Creating configuration...[/]");

        var config = new Dictionary<string, object?>
        {
            ["meta"] = new Dictionary<string, object?>
            {
                ["lastTouchedVersion"] = ThisAssembly.AssemblyVersion
            },
            ["wizard"] = new Dictionary<string, object?>
            {
                ["lastRunAt"] = DateTime.UtcNow.ToString("O"),
                ["lastRunVersion"] = ThisAssembly.AssemblyVersion,
                ["lastRunCommand"] = "interactive"
            },
            ["agents"] = new Dictionary<string, object?>
            {
                [agentName] = new Dictionary<string, object?>
                {
                    ["name"] = agentName,
                    ["workspace"] = Path.Combine(_aetherDir, "workspaces", agentName),
                    ["enabled"] = true
                }
            }
        };

        if (providerKey.Length > 0)
        {
            config["providers"] = new Dictionary<string, object?>
            {
                [providerKey] = new Dictionary<string, object?>
                {
                    ["type"] = providerKey == "anthropic" ? "anthropic" : "openai",
                    ["model"] = model,
                    ["api_key"] = apiKey ?? ""
                }
            };
        }

        if (!string.IsNullOrWhiteSpace(botToken))
        {
            config["channels"] = new Dictionary<string, object?>
            {
                ["telegram"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["bot_token"] = botToken
                }
            };
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct);

        // Scaffold workspace
        var workspacePath = Path.Combine(_aetherDir, "workspaces", agentName);
        await ScaffoldWorkspaceAsync(agentName, workspacePath, ct);

        // Install systemd service if requested
        if (installService)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Setting up WSL service...[/]");
            var installed = await TryInstallServiceAsync(agentName, ct);
            if (installed)
                AnsiConsole.MarkupLine("[green]Service installed. Aether will start automatically on WSL boot.[/]");
            else
                AnsiConsole.MarkupLine("[yellow]Service setup skipped — run scripts/install-service.sh manually later.[/]");
        }

        // Completion summary
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold green]Setup Complete[/]").RuleStyle(Style.Parse("green")));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Provider", providerKey.Length > 0 ? providerKey : "skipped");
        table.AddRow("Model", model.Length > 0 ? model : "(not set)");
        table.AddRow("Agent", agentName);
        table.AddRow("Workspace", Path.Combine(_aetherDir, "workspaces", agentName));
        table.AddRow("Telegram", telegramSetup ? "configured" : "skipped");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Next steps:[/]");
        AnsiConsole.MarkupLine($"  Run [blue]./run.sh serve[/] to start the agent runtime");
        AnsiConsole.MarkupLine($"\nConfig saved to [grey]{configPath}[/]");
    }

    private static async Task ScaffoldWorkspaceAsync(string name, string workspacePath, CancellationToken ct)
    {
        // Create workspace directory (idempotent)
        Directory.CreateDirectory(workspacePath);
        Directory.CreateDirectory(Path.Combine(workspacePath, "memory"));

        var templates = new Dictionary<string, string>
        {
            ["SOUL.md"] = $"# {name} — Soul\n\nYou are {name}, an Aether agent.\n\n## Tone\nFriendly, helpful, concise.\n\n## Rules\n- Stay in character\n- Be helpful\n- Don't make things up\n",
            ["USER.md"] = $"# User Profile\n\nName: User\nTimezone: UTC\n",
            ["IDENTITY.md"] = $"# Identity\n\nName: {name}\nCreature: Digital agent\nVibe: Helpful companion\n",
            ["MEMORY.md"] = $"# Memory\n\n## User\n\n## Agent Context\n\n## Multi-Agent Ecosystem\n",
            ["HEARTBEAT.md"] = "HEARTBEAT_OK\n\n- Check TASK_INBOX.md\n",
            ["TASK_INBOX.md"] = "# Task Inbox\n",
            ["TASK_REPORT.md"] = "# Task Report\n",
            ["DREAMS.md"] = "# Dreams\n",
            ["INTROSPECTION.md"] = "# Introspection\n",
            ["AGENTS_GUARD.md"] = "# Agent Guard\n\n## Configuration Isolation\n- Each agent's config is independent\n\n## Red Lines\n- Don't modify other agents' files\n\n## Anti-Hang\n- Report if stuck for more than 3 attempts\n\n## State Recovery\n- Check MEMORY.md on startup\n",
            ["AGENTS.md"] = "# Agent Configuration\n\nModel: (set in .aether.json)\nHeartbeat: (set in .aether.json)\n\n## Channels\n- Active channels and bindings are configured in ~/.aether/config.json\n",
            [".aether.json"] = "{\n  \"model\": { \"primary\": null, \"fallbacks\": [] },\n  \"heartbeat\": { \"intervalMinutes\": 60 },\n  \"agent\": { \"name\": \"\" }\n}\n"
        };

        foreach (var (file, content) in templates)
        {
            var path = Path.Combine(workspacePath, file);
            if (!File.Exists(path))
                await File.WriteAllTextAsync(path, content, ct);
        }

        AnsiConsole.MarkupLine($"[grey]Workspace scaffolded: {workspacePath}[/]");
    }

    private static bool DetectWsl()
    {
        try
        {
            return File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText("/proc/version").Contains("WSL", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryInstallServiceAsync(string agentName, CancellationToken ct)
    {
        // Walk up from the binary directory to find the repo root (has scripts/install-service.sh)
        var dir = AppContext.BaseDirectory;
        string? repoRoot = null;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "scripts", "install-service.sh")))
            {
                repoRoot = dir;
                break;
            }
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        if (repoRoot is null)
            return false;

        var installScript = Path.Combine(repoRoot, "scripts", "install-service.sh");

        try
        {
            var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sudo",
                    Arguments = $"bash {installScript} install",
                    UseShellExecute = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private Dictionary<string, object?> BuildMinimalConfig(string command)
    {
        // Read API key from env vars for non-interactive mode
        var openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                         ?? Environment.GetEnvironmentVariable("AETHER_PROVIDERS_OPENROUTER_API_KEY")
                         ?? "";
        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                         ?? Environment.GetEnvironmentVariable("AETHER_PROVIDERS_ANTHROPIC_API_KEY")
                         ?? "";
        var fireworksKey = Environment.GetEnvironmentVariable("FIREWORKS_API_KEY")
                         ?? Environment.GetEnvironmentVariable("AETHER_PROVIDERS_FIREWORKS_API_KEY")
                         ?? "";
        var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                    ?? Environment.GetEnvironmentVariable("AETHER_CHANNELS_TELEGRAM_BOT_TOKEN")
                    ?? "";

        return new Dictionary<string, object?>
        {
            ["wizard"] = new Dictionary<string, object?>
            {
                ["lastRunAt"] = DateTime.UtcNow.ToString("O"),
                ["lastRunVersion"] = ThisAssembly.AssemblyVersion,
                ["lastRunCommand"] = command
            },
            ["meta"] = new Dictionary<string, object?>
            {
                ["lastTouchedVersion"] = ThisAssembly.AssemblyVersion
            },
            ["providers"] = new Dictionary<string, object?>
            {
                ["openrouter"] = new Dictionary<string, object?>
                {
                    ["type"] = "openai",
                    ["model"] = "nvidia/nemotron-3-super-120b-a12b:free",
                    ["api_key"] = openRouterKey
                },
                ["fireworks"] = new Dictionary<string, object?>
                {
                    ["type"] = "openai",
                    ["model"] = "accounts/fireworks/routers/kimi-k2p5-turbo",
                    ["api_key"] = fireworksKey
                },
                ["anthropic"] = new Dictionary<string, object?>
                {
                    ["type"] = "anthropic",
                    ["model"] = "claude-sonnet-4-6",
                    ["api_key"] = anthropicKey
                }
            },
            ["agents"] = new Dictionary<string, object?>
            {
                ["default"] = new Dictionary<string, object?>
                {
                    ["name"] = "default",
                    ["workspace"] = Path.Combine(_aetherDir, "workspaces", "default"),
                    ["enabled"] = true,
                    ["bindings"] = new[] { "telegram:6713734957" }
                }
            },
            ["channels"] = new Dictionary<string, object?>
            {
                ["telegram"] = new Dictionary<string, object?>
                {
                    ["enabled"] = !string.IsNullOrWhiteSpace(botToken),
                    ["bot_token"] = botToken
                }
            }
        };
    }
}

internal static class ThisAssembly
{
    public const string AssemblyVersion = "0.1.0";
}
