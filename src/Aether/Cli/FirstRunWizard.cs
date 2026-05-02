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
        if (interactive && Environment.UserInteractive)
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
        var agentName = AnsiConsole.Prompt(
            new TextPrompt<string>("Agent name:")
                .DefaultValue("default")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(agentName))
            agentName = "default";

        // Step 3: Model (if provider selected)
        string model = "";
        if (providerKey.Length > 0)
        {
            var modelInput = AnsiConsole.Prompt(
                new TextPrompt<string>($"Model name [{providerKey}]:")
                    .DefaultValue(providerKey switch
                    {
                        "openrouter" => "nvidia/nemotron-3-super-120b-a12b:free",
                        "anthropic" => "claude-sonnet-4-6",
                        "fireworks" => "accounts/fireworks/models/deepseek-v3-0324",
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
        AnsiConsole.MarkupLine($"  Run [blue]aether agent add {agentName}[/] to scaffold the agent workspace");
        AnsiConsole.MarkupLine($"  Or [blue]aether serve[/] to start the agent runtime");
        AnsiConsole.MarkupLine($"\nConfig saved to [grey]{configPath}[/]");
    }

    private Dictionary<string, object?> BuildMinimalConfig(string command)
    {
        return new Dictionary<string, object?>
        {
            ["llm"] = new Dictionary<string, object?>
            {
                ["provider"] = "openrouter",
                ["model"] = ""
            },
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
            ["agents"] = new Dictionary<string, object?>
            {
                ["default"] = new Dictionary<string, object?>
                {
                    ["name"] = "default",
                    ["workspace"] = Path.Combine(_aetherDir, "workspaces", "default"),
                    ["enabled"] = true
                }
            }
        };
    }
}

internal static class ThisAssembly
{
    public const string AssemblyVersion = "0.1.0";
}
