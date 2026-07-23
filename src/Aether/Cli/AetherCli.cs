using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Aether.Agents;
using Aether.Channels;
using Aether.Config;
using Aether.Providers;
using Aether.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace Aether.Cli;

public sealed class AetherCli
{
    private readonly string _aetherDir;
    private readonly AgentWorkspaceScaffolder _scaffolder;
    private readonly AgentAuthProfiles _authProfiles;
    private readonly ILogger<AetherCli> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PluginCli _pluginCli;

    public AetherCli(
        string aetherDir,
        AgentWorkspaceScaffolder scaffolder,
        AgentAuthProfiles authProfiles,
        ILogger<AetherCli> logger)
    {
        _aetherDir = aetherDir;
        _scaffolder = scaffolder;
        _authProfiles = authProfiles;
        _logger = logger;
        _pluginCli = new PluginCli(aetherDir,
            NullLogger<PluginCli>.Instance);
    }

    public RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Aether — agent framework CLI");

        root.AddCommand(BuildAgentCommand());
        root.AddCommand(BuildPluginCommand());
        root.AddCommand(BuildProviderCommand());
        root.AddCommand(BuildAccessCommand());
        root.AddCommand(BuildIntegrityCommand());
        root.AddCommand(BuildGatewayCommand());
        root.AddCommand(BuildRestartCommand());

        return root;
    }

    private Command BuildGatewayCommand()
    {
        var gateway = new Command("gateway", "Manage the Aether gateway service");

        gateway.AddCommand(BuildGatewayStatusCommand());
        gateway.AddCommand(BuildGatewayRestartCommand());

        return gateway;
    }

    private Command BuildGatewayStatusCommand()
    {
        var cmd = new Command("status", "Check the Aether service status");

        cmd.SetHandler(async (context) =>
        {
            // 1. Try systemd (Linux)
            var isUser = await IsSystemdServiceActiveAsync(user: true);
            if (isUser)
            {
                context.Console.Out.Write("Aether service is running (user-level systemd).\n");
                await RunSystemdCommandAsync("status", user: true, context);
                return;
            }

            var isSystem = await IsSystemdServiceActiveAsync(user: false);
            if (isSystem)
            {
                context.Console.Out.Write("Aether service is running (system-level systemd).\n");
                await RunSystemdCommandAsync("status", user: false, context);
                return;
            }

            // 2. Try macOS launchd
            var launchdPid = await GetLaunchdPidAsync();
            if (launchdPid.HasValue)
            {
                context.Console.Out.Write($"Aether service is running via launchd (PID: {launchdPid}).\n");
                return;
            }

            // 3. Try bare process
            var barePid = await GetBareProcessPidAsync();
            if (barePid.HasValue)
            {
                context.Console.Out.Write($"Aether is running as a bare process (PID: {barePid}).\n");
                context.Console.Out.Write("Restart: aether gateway restart\n");
                return;
            }

            context.Console.Out.Write("Aether service is not running.\n");
        });

        return cmd;
    }

    private Command BuildGatewayRestartCommand()
    {
        var cmd = new Command("restart", "Restart the Aether service");

        cmd.SetHandler(async (context) =>
        {
            // 1. Try systemd (Linux)
            var isUser = await IsSystemdServiceActiveAsync(user: true);
            if (isUser)
            {
                context.Console.Out.Write("Restarting Aether service (user-level systemd)...\n");
                await RunSystemdCommandAsync("restart", user: true, context);
                return;
            }

            var isSystem = await IsSystemdServiceActiveAsync(user: false);
            if (isSystem)
            {
                context.Console.Out.Write("Restarting Aether service (system-level systemd)...\n");
                await RunSystemdCommandAsync("restart", user: false, context);
                return;
            }

            // 2. Try macOS launchd
            var launchdPid = await GetLaunchdPidAsync();
            if (launchdPid.HasValue)
            {
                context.Console.Out.Write("Restarting Aether service via launchd...\n");
                await RunLaunchctlCommandAsync("restart", context);
                return;
            }

            // 3. Try bare process: kill and respawn
            var barePid = await GetBareProcessPidAsync();
            if (barePid.HasValue)
            {
                context.Console.Out.Write($"Restarting Aether bare process (PID: {barePid})...\n");

                // Kill the running process
                try
                {
                    System.Diagnostics.Process.Start("kill", barePid.Value.ToString());
                    await Task.Delay(1000, context.GetCancellationToken());
                }
                catch (Exception ex)
                {
                    context.Console.Error.Write($"Failed to stop Aether: {ex.Message}\n");
                }
            }

            // 4. Spawn fresh (handles both restart after kill, and cold start)
            await SpawnAetherAsync(context);
        });

        return cmd;
    }

    private async Task<bool> IsSystemdServiceActiveAsync(bool user)
    {
        var args = user ? "--user is-active aether.service" : "is-active aether.service";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "systemctl",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var check = System.Diagnostics.Process.Start(psi);
            if (check is null) return false;
            var output = (await check.StandardOutput.ReadToEndAsync()).Trim();
            await check.WaitForExitAsync();
            return output == "active";
        }
        catch { return false; }
    }

    private async Task RunSystemdCommandAsync(string command, bool user, System.CommandLine.Invocation.InvocationContext context)
    {
        var fileName = (user || command == "status") ? "systemctl" : "sudo";
        var args = user 
            ? $"--user {command} aether.service" 
            : (command == "status" ? "status aether.service" : $"systemctl {command} aether.service");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is not null)
        {
            await process.WaitForExitAsync(context.GetCancellationToken());
            if (process.ExitCode != 0)
            {
                context.Console.Error.Write($"{fileName} {args} failed with exit code {process.ExitCode}\n");
                context.ExitCode = 1;
            }
        }
    }

    private async Task<int?> GetLaunchdPidAsync()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "launchctl",
            Arguments = "list com.thoor.aether",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            // launchctl list output: PID Status Label
            var parts = output.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && int.TryParse(parts[0], out var pid) && pid > 0)
                return pid;

            return null;
        }
        catch { return null; }
    }

    private async Task<int?> GetBareProcessPidAsync()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pgrep",
            Arguments = "-f \"Aether.dll serve\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
            await process.WaitForExitAsync();

            if (int.TryParse(output.Split('\n')[0].Trim(), out var pid) && pid > 0)
                return pid;

            return null;
        }
        catch { return null; }
    }

    private async Task RunLaunchctlCommandAsync(string command, System.CommandLine.Invocation.InvocationContext context)
    {
        // launchctl doesn't have a "restart" — need unload + load
        var plist = $"{Environment.GetEnvironmentVariable("HOME")}/Library/LaunchAgents/com.thoor.aether.plist";

        if (!File.Exists(plist))
        {
            context.Console.Error.Write($"LaunchAgent plist not found at {plist}\n");
            context.ExitCode = 1;
            return;
        }

        // Unload
        var unloadPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "launchctl",
            Arguments = $"unload \"{plist}\"",
            UseShellExecute = false
        };
        using (var unload = System.Diagnostics.Process.Start(unloadPsi))
        {
            if (unload is not null)
                await unload.WaitForExitAsync(context.GetCancellationToken());
        }

        await Task.Delay(500, context.GetCancellationToken());

        // Load
        var loadPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "launchctl",
            Arguments = $"load \"{plist}\"",
            UseShellExecute = false
        };
        using (var load = System.Diagnostics.Process.Start(loadPsi))
        {
            if (load is not null)
                await load.WaitForExitAsync(context.GetCancellationToken());
        }

        context.Console.Out.Write("Aether service restarted via launchd.\n");
    }

    private async Task SpawnAetherAsync(System.CommandLine.Invocation.InvocationContext context)
    {
        var repoDir = Environment.GetEnvironmentVariable("AETHER_REPO") ?? "/Users/thoor/work/aether";
        var bashArgs = $"-c \"cd '{repoDir}' && nohup dotnet run --project '{repoDir}/src/Aether/Aether.csproj' -- serve > /dev/null 2>&1 &\"";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = bashArgs,
            UseShellExecute = false
        };

        try
        {
            System.Diagnostics.Process.Start(psi);
            context.Console.Out.Write("Aether started.\n");
        }
        catch (Exception ex)
        {
            context.Console.Error.Write($"Failed to start Aether: {ex.Message}\n");
            context.Console.Out.Write("Start manually: dotnet run -- serve\n");
            context.ExitCode = 1;
        }
    }

    private Command BuildPluginCommand() => _pluginCli.BuildPluginCommand();

    private Command BuildProviderCommand()
    {
        var provider = new Command("provider", "Manage LLM providers");
        provider.AddCommand(BuildProviderAddCommand());
        provider.AddCommand(BuildProviderListCommand());
        return provider;
    }

    private Command BuildProviderAddCommand()
    {
        var modeOpt = new Option<string>("--mode", "Onboarding mode: import | raw | oauth");
        modeOpt.SetDefaultValue("import");
        var templateOpt = new Option<string?>("--template", "Template id to import (import mode)");
        var nameOpt = new Option<string?>("--name", "Provider name (raw mode, or override import name)");
        var urlOpt = new Option<string?>("--url", "Base URL (raw mode)");
        var typeOpt = new Option<string?>("--type", "Provider type: openai | anthropic (raw mode)");
        var apiKeyOpt = new Option<string?>("--api-key", "API key (overrides env resolution)");
        var modelsOpt = new Option<string?>("--models", "Comma-separated model list (raw mode)");
        var providersDirOpt = new Option<string?>("--providers-dir", "Override ~/.anima/providers.d (for import)");
        var animaEnvOpt = new Option<string?>("--anima-env", "Override ~/.anima/anima.env path");
        var nonInteractiveOpt = new Option<bool>("--non-interactive", "Skip prompts; error if key missing");

        var cmd = new Command("add", "Add an LLM provider (import from providers.d, raw, or oauth)")
        {
            modeOpt, templateOpt, nameOpt, urlOpt, typeOpt,
            apiKeyOpt, modelsOpt, providersDirOpt, animaEnvOpt, nonInteractiveOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var mode = context.ParseResult.GetValueForOption(modeOpt) ?? "import";
            var ct = context.GetCancellationToken();

            if (string.Equals(mode, "raw", StringComparison.OrdinalIgnoreCase))
            {
                await HandleProviderAddRawAsync(context, nameOpt, urlOpt, typeOpt, apiKeyOpt, modelsOpt, ct);
                return;
            }

            // import (default) and oauth both resolve via providers.d templates
            await HandleProviderAddImportAsync(context, templateOpt, nameOpt, apiKeyOpt, providersDirOpt, animaEnvOpt, nonInteractiveOpt, ct);
        });

        return cmd;
    }

    private async Task HandleProviderAddImportAsync(
        InvocationContext context,
        Option<string?> templateOpt,
        Option<string?> nameOpt,
        Option<string?> apiKeyOpt,
        Option<string?> providersDirOpt,
        Option<string?> animaEnvOpt,
        Option<bool> nonInteractiveOpt,
        CancellationToken ct)
    {
        var templateId = context.ParseResult.GetValueForOption(templateOpt);
        var nameOverride = context.ParseResult.GetValueForOption(nameOpt);
        var explicitKey = context.ParseResult.GetValueForOption(apiKeyOpt);
        var providersDir = context.ParseResult.GetValueForOption(providersDirOpt);
        var animaEnvPath = context.ParseResult.GetValueForOption(animaEnvOpt);
        var nonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOpt);

        var templates = TemplateScanner.ScanTemplates(providersDir);
        if (templates.Count == 0)
        {
            context.Console.Out.Write("No provider templates found in providers.d. Use --mode raw for manual setup.");
            context.ExitCode = 1;
            return;
        }

        if (string.IsNullOrEmpty(templateId))
        {
            context.Console.Out.Write("Available templates:");
            foreach (var t in templates)
                context.Console.Out.Write($"  {t.Id}  ({t.Label})  [{(t.Supported ? t.Api : "unsupported")}]");
            context.Console.Out.Write("Specify --template <id> to import.");
            context.ExitCode = 1;
            return;
        }

        var template = templates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
        if (template is null)
        {
            context.Console.Out.Write($"Template '{templateId}' not found in providers.d.");
            context.ExitCode = 1;
            return;
        }

        if (!template.Supported)
        {
            context.Console.Out.Write(
                $"Provider '{template.Id}' uses unsupported API format '{template.Api}'. " +
                "Use --mode raw with an OpenAI-compatible proxy endpoint instead.");
            context.ExitCode = 1;
            return;
        }

        // resolve key
        string apiKey;
        if (!string.IsNullOrEmpty(explicitKey))
        {
            apiKey = explicitKey!;
        }
        else
        {
            var envOptions = new EnvResolveOptions { AnimaEnvPath = animaEnvPath };
            var resolved = EnvResolver.ResolveApiKeyRef(template.ApiKeyRef, envOptions);
            if (resolved.IsOAuth)
            {
                context.Console.Out.Write(
                    $"Provider '{template.Id}' requires OAuth login for '{resolved.OAuthProvider}'. " +
                    "OAuth flow is not yet implemented. Use --api-key to provide a manual key.");
                context.ExitCode = 1;
                return;
            }
            if (!resolved.Resolved || string.IsNullOrEmpty(resolved.Value))
            {
                if (nonInteractive)
                {
                    context.Console.Out.Write(
                        $"No API key found for '{template.ApiKeyRef}'. " +
                        "Set the env var or use --api-key to provide one.");
                    context.ExitCode = 1;
                    return;
                }

                // interactive prompt (masked)
                apiKey = AnsiConsole.Prompt(
                    new TextPrompt<string>($"Enter API key for [yellow]{template.Id}[/]:")
                        .Secret());
            }
            else
            {
                apiKey = resolved.Value!;
            }
        }

        var providerName = string.IsNullOrEmpty(nameOverride) ? template.Id : nameOverride!;
        await ProviderRegistrar.WriteProviderAsync(_aetherDir, providerName, template, apiKey, ct);
        context.Console.Out.Write(
            $"Provider '{providerName}' added (type={template.MappedType}, baseUrl={template.BaseUrl}, models={template.Models.Count}).");
    }

    private async Task HandleProviderAddRawAsync(
        InvocationContext context,
        Option<string?> nameOpt,
        Option<string?> urlOpt,
        Option<string?> typeOpt,
        Option<string?> apiKeyOpt,
        Option<string?> modelsOpt,
        CancellationToken ct)
    {
        var name = context.ParseResult.GetValueForOption(nameOpt);
        var url = context.ParseResult.GetValueForOption(urlOpt);
        var type = context.ParseResult.GetValueForOption(typeOpt);
        var apiKey = context.ParseResult.GetValueForOption(apiKeyOpt);
        var modelsRaw = context.ParseResult.GetValueForOption(modelsOpt);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(type))
        {
            context.Console.Out.Write("Raw mode requires --name, --url, and --type.");
            context.ExitCode = 1;
            return;
        }

        var normalizedType = type!.ToLowerInvariant();
        if (normalizedType is not ("openai" or "anthropic"))
        {
            context.Console.Out.Write($"Unsupported type '{type}'. Use 'openai' or 'anthropic'.");
            context.ExitCode = 1;
            return;
        }

        var models = (modelsRaw ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var template = new ProviderTemplate
        {
            Id = name!,
            Label = name!,
            Api = normalizedType == "anthropic" ? "anthropic-messages" : "openai-completions",
            MappedType = normalizedType,
            BaseUrl = url!,
            ApiKeyRef = apiKey ?? "",
            Models = models
        };

        await ProviderRegistrar.WriteProviderAsync(_aetherDir, name!, template, apiKey ?? "", ct);
        context.Console.Out.Write($"Provider '{name}' added (type={normalizedType}, baseUrl={url}, models={models.Count}).");
    }

    private Command BuildProviderListCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");
        var providersDirOpt = new Option<string?>("--providers-dir", "Override ~/.anima/providers.d");
        var animaEnvOpt = new Option<string?>("--anima-env", "Override ~/.anima/anima.env path");

        var cmd = new Command("list", "List provider templates from providers.d")
        {
            jsonOpt, providersDirOpt, animaEnvOpt
        };

        cmd.SetHandler((context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(jsonOpt);
            var providersDir = context.ParseResult.GetValueForOption(providersDirOpt);
            var animaEnvPath = context.ParseResult.GetValueForOption(animaEnvOpt);

            var templates = TemplateScanner.ScanTemplates(providersDir);
            var envOptions = new EnvResolveOptions { AnimaEnvPath = animaEnvPath };

            var rows = templates
                .Select(t =>
                {
                    var resolved = EnvResolver.ResolveApiKeyRef(t.ApiKeyRef, envOptions);
                    var status = !t.Supported ? "unsupported"
                        : resolved.IsOAuth ? "oauth"
                        : resolved.Resolved ? "found"
                        : "missing";
                    return new { t.Id, t.Label, t.Api, t.BaseUrl, Status = status, Supported = t.Supported };
                })
                .OrderBy(r => r.Label)
                .ToList();

            if (asJson)
            {
                var json = JsonSerializer.Serialize(rows, JsonOptions);
                context.Console.Out.Write(json);
                return;
            }

            if (rows.Count == 0)
            {
                context.Console.Out.Write("No provider templates found in providers.d.");
                return;
            }

            foreach (var r in rows)
            {
                var mark = r.Status switch
                {
                    "found" => "✅ found",
                    "missing" => "⚠️ missing",
                    "oauth" => "🔑 oauth",
                    _ => "⛔ unsupported"
                };
                context.Console.Out.Write($"{r.Label} ({r.Id})  api={r.Api}  [{mark}]  baseUrl={r.BaseUrl}");
            }
        });

        return cmd;
    }

    private Command BuildAgentCommand()
    {
        var agent = new Command("agent", "Manage Aether agents");

        agent.AddCommand(BuildAgentAddCommand());
        agent.AddCommand(BuildAgentListCommand());
        agent.AddCommand(BuildAgentDeleteCommand());
        agent.AddCommand(BuildAgentSetIdentityCommand());
        agent.AddCommand(BuildAgentBindCommand());
        agent.AddCommand(BuildAgentUnbindCommand());

        return agent;
    }

    private Command BuildAgentAddCommand()
    {
        var nameArg = new Argument<string>("name", "Agent name");
        var modelOpt = new Option<string?>("--model", "Primary model for the agent");
        var nonInteractiveOpt = new Option<bool>("--non-interactive", "Skip interactive prompts");
        var workspaceOpt = new Option<string?>("--workspace", "Custom workspace path");

        var cmd = new Command("add", "Add a new agent")
        {
            nameArg, modelOpt, nonInteractiveOpt, workspaceOpt
        };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var model = context.ParseResult.GetValueForOption(modelOpt);
            var nonInteractive = context.ParseResult.GetValueForOption(nonInteractiveOpt);
            var workspaceOverride = context.ParseResult.GetValueForOption(workspaceOpt);

            var workspacePath = workspaceOverride ?? Path.Combine(_aetherDir, "workspaces", name);

            await _scaffolder.ScaffoldAsync(name, workspacePath, interactive: !nonInteractive);
            await _authProfiles.CreateAuthDirectoryAsync(name, context.GetCancellationToken());

            if (!string.IsNullOrEmpty(model))
            {
                var auth = await _authProfiles.LoadAuthProfilesAsync(name, context.GetCancellationToken());
                auth = auth with { State = auth.State with { ActiveModel = model } };
                await _authProfiles.SaveAuthProfilesAsync(name, auth, context.GetCancellationToken());
            }

            await UpdateConfigAgentAsync(name, workspacePath, context.GetCancellationToken());

            context.Console.Out.Write($"Agent '{name}' added at {workspacePath}");
        });

        return cmd;
    }

    private Command BuildAgentListCommand()
    {
        var jsonOpt = new Option<bool>("--json", "Output as JSON");

        var cmd = new Command("list", "List agents") { jsonOpt };

        cmd.SetHandler(async (context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(jsonOpt);
            var configPath = Path.Combine(_aetherDir, "config.json");

            if (!File.Exists(configPath))
            {
                if (asJson) context.Console.Out.Write("[]");
                else context.Console.Out.Write("No agents configured.");
                return;
            }

            var json = await File.ReadAllTextAsync(configPath, context.GetCancellationToken());
            using var doc = JsonDocument.Parse(json);

            if (asJson)
            {
                if (doc.RootElement.TryGetProperty("agents", out var agentsEl))
                    context.Console.Out.Write(agentsEl.GetRawText());
                else
                    context.Console.Out.Write("[]");
                return;
            }

            if (!doc.RootElement.TryGetProperty("agents", out var agents))
            {
                AnsiConsole.MarkupLine("[dim]No agents configured.[/]");
                return;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Name[/]"))
                .AddColumn(new TableColumn("[bold]Display Name[/]"))
                .AddColumn(new TableColumn("[bold]Status[/]"));

            foreach (var prop in agents.EnumerateObject())
            {
                var enabled = prop.Value.TryGetProperty("enabled", out var en) && en.GetBoolean();
                var status = enabled ? "[green]enabled[/]" : "[dim]disabled[/]";
                var displayName = prop.Value.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                table.AddRow(
                    $"[violet]{Markup.Escape(prop.Name)}[/]",
                    Markup.Escape(displayName),
                    status);
            }

            AnsiConsole.Write(table);
        });

        return cmd;
    }

    private Command BuildAgentDeleteCommand()
    {
        var nameArg = new Argument<string>("name", "Agent name");
        var pruneOpt = new Option<bool>("--prune-workspace", "Also delete workspace directory");
        var forceOpt = new Option<bool>("--force", "Skip confirmation");

        var cmd = new Command("delete", "Delete an agent") { nameArg, pruneOpt, forceOpt };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var prune = context.ParseResult.GetValueForOption(pruneOpt);
            var force = context.ParseResult.GetValueForOption(forceOpt);

            if (!force)
            {
                context.Console.Out.Write($"Use --force to confirm deletion of agent '{name}'.");
                context.ExitCode = 1;
                return;
            }

            var workspacePath = Path.Combine(_aetherDir, "workspaces", name);
            if (prune && Directory.Exists(workspacePath))
                Directory.Delete(workspacePath, recursive: true);

            var agentPath = Path.Combine(_aetherDir, "agents", name);
            if (prune && Directory.Exists(agentPath))
                Directory.Delete(agentPath, recursive: true);

            await RemoveConfigAgentAsync(name, context.GetCancellationToken());

            context.Console.Out.Write($"Agent '{name}' deleted.");
        });

        return cmd;
    }

    private Command BuildAgentSetIdentityCommand()
    {
        var nameArg = new Argument<string>("name", "Agent name");
        var displayNameOpt = new Option<string?>("--display-name", "Display name");
        var emojiOpt = new Option<string?>("--emoji", "Emoji icon");
        var avatarOpt = new Option<string?>("--avatar", "Avatar path");

        var cmd = new Command("set-identity", "Set agent identity") { nameArg, displayNameOpt, emojiOpt, avatarOpt };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var displayName = context.ParseResult.GetValueForOption(displayNameOpt);
            var emoji = context.ParseResult.GetValueForOption(emojiOpt);
            var avatar = context.ParseResult.GetValueForOption(avatarOpt);

            var configPath = Path.Combine(_aetherDir, "config.json");
            if (!File.Exists(configPath))
            {
                context.Console.Out.Write($"Agent '{name}' not found in config.");
                context.ExitCode = 1;
                return;
            }

            var json = await File.ReadAllTextAsync(configPath, context.GetCancellationToken());
            using var doc = JsonDocument.Parse(json);
            var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions) ?? new();
            // Rebuild with updates
            var config = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions) ?? new();

            if (!config.TryGetValue("agents", out var agentsObj) || agentsObj is not JsonElement agentsEl)
            {
                context.Console.Out.Write($"Agent '{name}' not found in config.");
                context.ExitCode = 1;
                return;
            }

            // Use JsonElement deserialization
            var agents = DeserializeToDict(agentsEl);
            if (!agents.TryGetValue(name, out var agentEntry) || agentEntry is null)
            {
                context.Console.Out.Write($"Agent '{name}' not found in config.");
                context.ExitCode = 1;
                return;
            }

            var agentDict = agentEntry as Dictionary<string, object?> ?? new();
            if (displayName is not null) agentDict["displayName"] = displayName;
            if (emoji is not null) agentDict["emoji"] = emoji;
            if (avatar is not null) agentDict["avatar"] = avatar;
            agents[name] = agentDict;

            config["agents"] = agents;
            var updated = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(configPath, updated, context.GetCancellationToken());

            context.Console.Out.Write($"Identity updated for '{name}'.");
        });

        return cmd;
    }

    private Command BuildAgentBindCommand()
    {
        var nameArg = new Argument<string>("name", "Agent name");
        var channelOpt = new Option<string?>("--channel", "Channel binding (e.g., telegram:12345)");

        var cmd = new Command("bind", "Manage agent channel bindings") { nameArg, channelOpt };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var channel = context.ParseResult.GetValueForOption(channelOpt);

            if (channel is null)
            {
                await ListBindingsAsync(name, context);
                return;
            }

            await ModifyBindingAsync(name, channel, add: true, context);
            context.Console.Out.Write($"Bound '{name}' to {channel}.");
        });

        return cmd;
    }

    private Command BuildAgentUnbindCommand()
    {
        var nameArg = new Argument<string>("name", "Agent name");
        var channelOpt = new Option<string?>("--channel", "Channel binding to remove");

        var cmd = new Command("unbind", "Remove agent channel binding") { nameArg, channelOpt };

        cmd.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForArgument(nameArg);
            var channel = context.ParseResult.GetValueForOption(channelOpt);

            if (channel is null)
            {
                context.Console.Out.Write("Specify --channel to remove a binding.");
                context.ExitCode = 1;
                return;
            }

            await ModifyBindingAsync(name, channel, add: false, context);
            context.Console.Out.Write($"Unbound '{name}' from {channel}.");
        });

        return cmd;
    }

    private Command BuildAccessCommand()
    {
        var access = new Command("access", "Manage channel access control");

        access.AddCommand(BuildAccessPairCommand());
        access.AddCommand(BuildAccessPolicyCommand());

        return access;
    }

    private Command BuildAccessPairCommand()
    {
        var codeArg = new Argument<string>("code", "Pairing code from the bot");
        var channelOpt = new Option<string>("--channel", () => "telegram", "Channel name");

        var cmd = new Command("pair", "Approve a pairing request") { codeArg, channelOpt };

        cmd.SetHandler(async (context) =>
        {
            var code = context.ParseResult.GetValueForArgument(codeArg);
            var channel = context.ParseResult.GetValueForOption(channelOpt);

            var access = new ChannelAccess(channel!, _aetherDir, NullLogger<ChannelAccess>.Instance);
            await access.LoadAsync(context.GetCancellationToken());

            if (await access.ApprovePairingAsync(code, context.GetCancellationToken()))
                context.Console.Out.Write($"Pairing approved. Sender added to {channel} allowlist.");
            else
            {
                context.Console.Out.Write("Invalid or expired pairing code.");
                context.ExitCode = 1;
            }
        });

        return cmd;
    }

    private Command BuildAccessPolicyCommand()
    {
        var policyArg = new Argument<string>("policy", "Access policy: open, pairing, or allowlist");
        var channelOpt = new Option<string>("--channel", () => "telegram", "Channel name");

        var cmd = new Command("policy", "Set access policy") { policyArg, channelOpt };

        cmd.SetHandler(async (context) =>
        {
            var policy = context.ParseResult.GetValueForArgument(policyArg);
            var channel = context.ParseResult.GetValueForOption(channelOpt);

            if (policy is not "open" and not "pairing" and not "allowlist")
            {
                context.Console.Out.Write("Policy must be: open, pairing, or allowlist.");
                context.ExitCode = 1;
                return;
            }

            var access = new ChannelAccess(channel!, _aetherDir, NullLogger<ChannelAccess>.Instance);
            await access.LoadAsync(context.GetCancellationToken());
            await access.SetModeAsync(policy, context.GetCancellationToken());

            context.Console.Out.Write($"Access policy for {channel} set to: {policy}");
        });

        return cmd;
    }

    private Command BuildIntegrityCommand()
    {
        var integrity = new Command("integrity", "Manage cryptographic identity and signing");

        integrity.AddCommand(BuildIntegrityInitCommand());
        integrity.AddCommand(BuildIntegritySignCommand());
        integrity.AddCommand(BuildIntegrityVerifyCommand());

        return integrity;
    }

    private Command BuildIntegrityInitCommand()
    {
        var agentOpt = new Option<string>("--agent", () => "maria", "Agent name");

        var cmd = new Command("init", "Generate keypair and sign boot files") { agentOpt };

        cmd.SetHandler(async (context) =>
        {
            var agentName = context.ParseResult.GetValueForOption(agentOpt);
            var agentDir = ResolveAgentDir(agentName!);
            if (agentDir is null) { context.ExitCode = 1; return; }

            var signer = new IntegritySigner(agentDir, NullLogger<IntegritySigner>.Instance);
            var publicKey = await signer.InitializeAsync(context.GetCancellationToken());
            context.Console.Out.Write($"Keypair generated for '{agentName}'.");

            var agentConfig = AgentConfigForDir(agentDir);
            var bootConfig = agentConfig.Boot ?? new BootConfig();
            await signer.SignBootFilesAsync(bootConfig, context.GetCancellationToken());
            context.Console.Out.Write($"Boot files signed. Integrity directory: {Path.Combine(agentDir, "_INTEGRITY")}");
        });

        return cmd;
    }

    private Command BuildIntegritySignCommand()
    {
        var fileArg = new Argument<string>("file", "File path relative to agent directory");
        var agentOpt = new Option<string>("--agent", () => "maria", "Agent name");

        var cmd = new Command("sign", "Sign a file") { fileArg, agentOpt };

        cmd.SetHandler(async (context) =>
        {
            var file = context.ParseResult.GetValueForArgument(fileArg);
            var agentName = context.ParseResult.GetValueForOption(agentOpt);
            var agentDir = ResolveAgentDir(agentName!);
            if (agentDir is null) { context.ExitCode = 1; return; }

            var signer = new IntegritySigner(agentDir, NullLogger<IntegritySigner>.Instance);
            var result = await signer.SignAsync(file, context.GetCancellationToken());

            if (result.Status == IntegrityStatus.Valid)
                context.Console.Out.Write($"Signed: {file}");
            else
            {
                context.Console.Out.Write($"Failed: {result.Error}");
                context.ExitCode = 1;
            }
        });

        return cmd;
    }

    private Command BuildIntegrityVerifyCommand()
    {
        var fileArg = new Argument<string?>("file", "File to verify (omit for all)");
        var agentOpt = new Option<string>("--agent", () => "maria", "Agent name");

        var cmd = new Command("verify", "Verify file signatures") { fileArg, agentOpt };

        cmd.SetHandler(async (context) =>
        {
            var file = context.ParseResult.GetValueForArgument(fileArg);
            var agentName = context.ParseResult.GetValueForOption(agentOpt);
            var agentDir = ResolveAgentDir(agentName!);
            if (agentDir is null) { context.ExitCode = 1; return; }

            var signer = new IntegritySigner(agentDir, NullLogger<IntegritySigner>.Instance);

            if (file is not null)
            {
                var result = await signer.VerifyAsync(file, context.GetCancellationToken());
                context.Console.Out.Write(result.Status switch
                {
                    IntegrityStatus.Valid => $"✓ {file} — valid",
                    IntegrityStatus.Unsigned => $"○ {file} — unsigned",
                    IntegrityStatus.Invalid => $"✗ {file} — INVALID: {result.Error}",
                    IntegrityStatus.KeyMissing => "✗ No public key found. Run 'aether integrity init' first.",
                    _ => "?"
                });
                if (result.Status is IntegrityStatus.Invalid or IntegrityStatus.KeyMissing)
                    context.ExitCode = 1;
            }
            else
            {
                var failures = await signer.VerifyAllAsync(context.GetCancellationToken());
                if (failures.Count == 0)
                    context.Console.Out.Write("All signed files verified.");
                else
                {
                    foreach (var (f, r) in failures)
                        context.Console.Out.Write($"✗ {f} — {r.Error}");
                    context.ExitCode = 1;
                }
            }
        });

        return cmd;
    }

    private Command BuildRestartCommand()
    {
        var cmd = new Command("restart", "Restart the Aether service");

        cmd.SetHandler(async (context) =>
        {
            // 1. Try systemd (Linux)
            var isUser = await IsSystemdServiceActiveAsync(user: true);
            if (isUser)
            {
                context.Console.Out.Write("Restarting Aether service (user-level systemd)...\n");
                await RunSystemdCommandAsync("restart", user: true, context);
                return;
            }

            var isSystem = await IsSystemdServiceActiveAsync(user: false);
            if (isSystem)
            {
                context.Console.Out.Write("Restarting Aether service (system-level systemd)...\n");
                await RunSystemdCommandAsync("restart", user: false, context);
                return;
            }

            // 2. Try macOS launchd
            var launchdPid = await GetLaunchdPidAsync();
            if (launchdPid.HasValue)
            {
                context.Console.Out.Write("Restarting Aether service via launchd...\n");
                await RunLaunchctlCommandAsync("restart", context);
                return;
            }

            // 3. Try bare process: kill and respawn
            var barePid = await GetBareProcessPidAsync();
            if (barePid.HasValue)
            {
                context.Console.Out.Write($"Restarting Aether bare process (PID: {barePid})...\n");

                try
                {
                    System.Diagnostics.Process.Start("kill", $"{barePid}");
                    await Task.Delay(1000, context.GetCancellationToken());
                }
                catch (Exception ex)
                {
                    context.Console.Error.Write($"Failed to stop Aether: {ex.Message}\n");
                }
            }

            // 4. Spawn fresh (handles both restart after kill, and cold start)
            await SpawnAetherAsync(context);
        });

        return cmd;
    }
    private string? ResolveAgentDir(string agentName)
    {
        // Check config.json workspace path first
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("agents", out var agents) &&
                    agents.TryGetProperty(agentName, out var entry) &&
                    entry.TryGetProperty("workspace", out var ws))
                {
                    var workspacePath = ws.GetString();
                    if (workspacePath is not null && Directory.Exists(workspacePath))
                        return workspacePath;
                }
            }
            catch { /* fall through to candidates */ }
        }

        // Try common locations
        var candidates = new[]
        {
            Path.Combine(_aetherDir, "workspaces", agentName)           // .aether/workspaces/<name>
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir)) return dir;
        }

        System.Console.Error.WriteLine($"Agent directory not found for '{agentName}'. Tried config workspace + {string.Join(", ", candidates)}");
        return null;
    }

    private static AgentConfig AgentConfigForDir(string agentDir)
    {
#pragma warning disable CS0618
        return new AgentConfig
        {
            Boot = new BootConfig
            {
                ConstitutionFiles = new() { "AGENTS_GUARD.md" },
                IdentityFiles = new() { "SOUL.md", "USER.md", "IDENTITY.md" }
            }
        };
#pragma warning restore CS0618
    }

    private async Task ListBindingsAsync(string agentName, System.CommandLine.Invocation.InvocationContext context)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(configPath))
        {
            context.Console.Out.Write("No bindings configured.");
            return;
        }

        var json = await File.ReadAllTextAsync(configPath, context.GetCancellationToken());
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("agents", out var agentsEl) ||
            !agentsEl.TryGetProperty(agentName, out var agent) ||
            !agent.TryGetProperty("bindings", out var bindings))
        {
            context.Console.Out.Write("No bindings configured.");
            return;
        }

        foreach (var binding in bindings.EnumerateArray())
            context.Console.Out.Write(binding.GetString() ?? "");
    }

    private async Task ModifyBindingAsync(string agentName, string channel, bool add, System.CommandLine.Invocation.InvocationContext context)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        Dictionary<string, object?> config;

        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath, context.GetCancellationToken());
            config = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions) ?? new();
        }
        else
        {
            config = new();
        }

        if (!config.TryGetValue("agents", out var agentsObj) || agentsObj is null)
        {
            config["agents"] = new Dictionary<string, object?>();
        }

        var agents = DeserializeToDict((JsonElement)(config["agents"]!));
        if (!agents.TryGetValue(agentName, out var agentEntry) || agentEntry is null)
        {
            context.Console.Out.Write($"Agent '{agentName}' not found. Add it first with 'aether agent add'.");
            context.ExitCode = 1;
            return;
        }

        var agentDict = agentEntry as Dictionary<string, object?> ?? new();
        var bindings = agentDict.TryGetValue("bindings", out var b) && b is JsonElement be && be.ValueKind == JsonValueKind.Array
            ? be.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : new List<string>();

        if (add)
        {
            if (!bindings.Contains(channel))
                bindings.Add(channel);
        }
        else
        {
            bindings.Remove(channel);
        }

        agentDict["bindings"] = bindings;
        agents[agentName] = agentDict;
        config["agents"] = agents;

        var updated = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, updated, context.GetCancellationToken());
    }

    private async Task UpdateConfigAgentAsync(string name, string workspacePath, CancellationToken ct)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        Dictionary<string, object?> config;

        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            config = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions) ?? new();
        }
        else
        {
            config = new();
        }

        var agents = config.TryGetValue("agents", out var a) && a is not null
            ? DeserializeToDict((JsonElement)a)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        agents[name] = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["workspace"] = workspacePath,
            ["enabled"] = true
        };

        config["agents"] = agents;
        var updated = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, updated, ct);
    }

    private async Task RemoveConfigAgentAsync(string name, CancellationToken ct)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(configPath)) return;

        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions) ?? new();

        if (config.TryGetValue("agents", out var a) && a is JsonElement el)
        {
            var agents = DeserializeToDict(el);
            agents.Remove(name);
            config["agents"] = agents;
        }

        var updated = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, updated, ct);
    }

    private static Dictionary<string, object?> DeserializeToDict(JsonElement el)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = JsonElementToObject(prop.Value);
        return dict;
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => DeserializeToDict(el),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => null
    };
}
