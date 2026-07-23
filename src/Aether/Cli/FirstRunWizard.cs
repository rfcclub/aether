using System.Text.Json;
using Aether.Providers;
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

        // Step 1: Onboarding mode (import from providers.d / raw / oauth)
        var modeChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to add a [green]provider[/]?")
                .PageSize(10)
                .AddChoices([
                    OnboardingFlow.ImportLabel,
                    OnboardingFlow.RawLabel,
                    OnboardingFlow.OAuthLabel
                ]));

        var mode = OnboardingFlow.ResolveModeFromChoice(modeChoice);

        string? providerKey = null;
        string? apiKey = null;
        string model = "";

        if (mode == OnboardingMode.Import)
        {
            (providerKey, apiKey, model) = await RunImportModeAsync(ct);
        }
        else if (mode == OnboardingMode.Raw)
        {
            (providerKey, apiKey, model) = await RunRawModeAsync(ct);
        }
        else // OAuth
        {
            (providerKey, apiKey, model) = await RunOAuthModeAsync(ct);
        }

        // Step 2: Agent name
        var agentNameInput = AnsiConsole.Prompt(
            new TextPrompt<string>("Agent name:")
                .DefaultValue("default")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(agentNameInput))
            agentNameInput = "default";

        var agentName = agentNameInput.Trim().ToLowerInvariant();

        // Step 3: Model (if provider selected) — pre-filled by mode flow, allow override
        if (providerKey is { Length: > 0 })
        {
            var modelInput = AnsiConsole.Prompt(
                new TextPrompt<string>($"Model name [[{providerKey}]]:")
                    .DefaultValue(string.IsNullOrEmpty(model) ? "" : model)
                    .AllowEmpty());
            if (!string.IsNullOrWhiteSpace(modelInput))
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

        // Build config — MERGE into existing config.json if present (the import/raw
        // modes may have already written a provider entry via ProviderRegistrar; we
        // must NOT overwrite it). We only set agents/channels/meta/wizard here and
        // preserve any existing providers map.
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Creating configuration...[/]");

        // configPath already declared at top of RunInteractiveAsync
        Dictionary<string, object?> config;
        if (File.Exists(configPath))
        {
            var existing = await File.ReadAllTextAsync(configPath, ct);
            config = JsonSerializer.Deserialize<Dictionary<string, object?>>(existing, JsonOptions) ?? new();
        }
        else
        {
            config = new Dictionary<string, object?>();
        }
        config["meta"] = new Dictionary<string, object?>
        {
            ["lastTouchedVersion"] = ThisAssembly.AssemblyVersion
        };
        config["wizard"] = new Dictionary<string, object?>
        {
            ["lastRunAt"] = DateTime.UtcNow.ToString("O"),
            ["lastRunVersion"] = ThisAssembly.AssemblyVersion,
            ["lastRunCommand"] = "interactive"
        };
        config["agents"] = new Dictionary<string, object?>
        {
            [agentName] = new Dictionary<string, object?>
            {
                ["name"] = agentName,
                ["workspace"] = Path.Combine(_aetherDir, "workspaces", agentName),
                ["enabled"] = true
            }
        };

        // NOTE: providers are owned by ProviderRegistrar (import/raw modes already wrote
        // the full entry with type/base_url/api_key/model/models). We do NOT set
        // config["providers"] here — that would overwrite the registrar's complete entry
        // with an incomplete one (missing base_url + models, wrong type).

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

        table.AddRow("Provider", !string.IsNullOrEmpty(providerKey) ? providerKey : "skipped");
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

    /// <summary>
    /// Import mode: scan providers.d, show template list with key status, auto-fill,
    /// resolve key (or prompt), then write via <see cref="ProviderRegistrar"/>.
    /// Returns (providerKey, apiKey, model) for the rest of the wizard to use.
    /// </summary>
    private async Task<(string? ProviderKey, string? ApiKey, string Model)> RunImportModeAsync(CancellationToken ct)
    {
        var rows = OnboardingFlow.ListImportableTemplates(providersDir: null, animaEnvPath: null);

        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No provider templates found in ~/.anima/providers.d. Use Raw mode instead.[/]");
            return (null, null, "");
        }

        // Build display labels with status indicators
        var labels = rows
            .Select(r =>
            {
                var mark = r.KeyStatus switch
                {
                    KeyStatus.Found => "[green]✅[/]",
                    KeyStatus.Missing => "[yellow]⚠️[/]",
                    KeyStatus.OAuth => "[blue]🔑[/]",
                    _ => "[red]⛔[/]"
                };
                return $"{mark}  {Markup.Escape(r.Label)} [dim]({Markup.Escape(r.Id)}, {Markup.Escape(r.Api)})[/]";
            })
            .ToList();

        var selectedLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a [green]provider template[/]:")
                .PageSize(15)
                .AddChoices(labels));

        var idx = labels.IndexOf(selectedLabel);
        var row = rows[idx];

        if (!row.Supported)
        {
            AnsiConsole.MarkupLine($"[red]This provider uses an unsupported API format ({row.Api}). Use Raw mode with an OpenAI-compatible proxy endpoint instead.[/]");
            return (null, null, "");
        }

        // Resolve key
        string? apiKey = null;
        if (row.KeyStatus == KeyStatus.OAuth)
        {
            AnsiConsole.MarkupLine($"[blue]Provider '{row.Id}' requires OAuth login for {row.Template.ApiKeyRef}.[/]");
            var oauthChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("OAuth login:")
                    .AddChoices(["Open browser to sign in", "Enter API key manually instead"]));

            if (oauthChoice == "Open browser to sign in")
            {
                AnsiConsole.MarkupLine("[yellow]OAuth login coming soon. Enter a key manually for now.[/]");
                apiKey = AnsiConsole.Prompt(new TextPrompt<string>("API key:").Secret().AllowEmpty());
            }
            else
            {
                apiKey = AnsiConsole.Prompt(new TextPrompt<string>("API key:").Secret().AllowEmpty());
            }
        }
        else if (row.KeyStatus == KeyStatus.Found)
        {
            var (resolved, foundKey) = OnboardingFlow.ResolveKeyForTemplate(row, animaEnvPath: null);
            var useFound = AnsiConsole.Confirm($"Use the key found in env ({foundKey?[..Math.Min(8, foundKey.Length)]}…)?", true);
            apiKey = useFound && !string.IsNullOrEmpty(foundKey) ? foundKey
                : AnsiConsole.Prompt(new TextPrompt<string>("Enter API key:").Secret().AllowEmpty());
        }
        else // Missing
        {
            AnsiConsole.MarkupLine($"[yellow]No key found for {row.Template.ApiKeyRef}.[/]");
            apiKey = AnsiConsole.Prompt(new TextPrompt<string>("Enter API key:").Secret().AllowEmpty());
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            AnsiConsole.MarkupLine("[yellow]No API key provided. You can add it later in ~/.aether/config.json[/]");
            return (null, null, "");
        }

        // Write provider via shared registrar
        await ProviderRegistrar.WriteProviderAsync(_aetherDir, row.Id, row.Template, apiKey!, ct);

        var providerKey = row.Id;
        var model = row.Template.Models.Count > 0 ? row.Template.Models[0] : "";
        return (providerKey, apiKey, model);
    }

    /// <summary>
    /// Raw mode: name → url → protocol → key → models → write via <see cref="ProviderRegistrar"/>.
    /// </summary>
    private async Task<(string? ProviderKey, string? ApiKey, string Model)> RunRawModeAsync(CancellationToken ct)
    {
        var name = AnsiConsole.Prompt(
            new TextPrompt<string>("Provider name:").AllowEmpty());
        if (string.IsNullOrWhiteSpace(name))
        {
            AnsiConsole.MarkupLine("[yellow]No provider name given.[/]");
            return (null, null, "");
        }
        name = name.Trim();

        var url = AnsiConsole.Prompt(
            new TextPrompt<string>("Base URL (https://…/v1):").AllowEmpty());
        if (string.IsNullOrWhiteSpace(url))
        {
            AnsiConsole.MarkupLine("[yellow]No URL given.[/]");
            return (null, null, "");
        }

        var protocolLabel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Protocol:")
                .AddChoices([
                    OnboardingFlow.ProtocolLabel(RawProtocol.OpenAiChat),
                    OnboardingFlow.ProtocolLabel(RawProtocol.OpenAiResponses),
                    OnboardingFlow.ProtocolLabel(RawProtocol.AnthropicMessages)
                ]));
        var protocol = OnboardingFlow.ParseProtocolLabel(protocolLabel);

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("API key:").Secret().AllowEmpty());

        var modelsInput = AnsiConsole.Prompt(
            new TextPrompt<string>("Models (comma-separated):")
                .DefaultValue("")
                .AllowEmpty());

        var template = OnboardingFlow.BuildRawTemplate(name, url.Trim(), protocol, apiKey ?? "", modelsInput);
        await ProviderRegistrar.WriteProviderAsync(_aetherDir, name, template, apiKey ?? "", ct);

        var model = template.Models.Count > 0 ? template.Models[0] : "";
        return (name, apiKey, model);
    }

    /// <summary>
    /// OAuth mode: placeholder. Shows "coming soon" and falls back to manual key entry.
    /// </summary>
    private async Task<(string? ProviderKey, string? ApiKey, string Model)> RunOAuthModeAsync(CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[yellow]OAuth login is coming soon. For now, enter a provider + key manually.[/]");
        // Reuse the raw flow as the manual fallback for OAuth providers.
        return await RunRawModeAsync(ct);
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
