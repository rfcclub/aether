using System.CommandLine;
using System.Text.Json;
using Aether.Agents;
using Aether.Channels;
using Aether.Config;
using Aether.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        root.AddCommand(BuildAccessCommand());
        root.AddCommand(BuildIntegrityCommand());
        root.AddCommand(BuildRestartCommand());

        return root;
    }

    private Command BuildPluginCommand() => _pluginCli.BuildPluginCommand();

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

            context.Console.WriteLine($"Agent '{name}' added at {workspacePath}");
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
                if (asJson) context.Console.WriteLine("[]");
                else context.Console.WriteLine("No agents configured.");
                return;
            }

            var json = await File.ReadAllTextAsync(configPath, context.GetCancellationToken());
            using var doc = JsonDocument.Parse(json);

            if (asJson)
            {
                if (doc.RootElement.TryGetProperty("agents", out var agentsEl))
                    context.Console.WriteLine(agentsEl.GetRawText());
                else
                    context.Console.WriteLine("[]");
                return;
            }

            if (!doc.RootElement.TryGetProperty("agents", out var agents))
            {
                context.Console.WriteLine("No agents configured.");
                return;
            }

            foreach (var prop in agents.EnumerateObject())
            {
                var enabled = prop.Value.TryGetProperty("enabled", out var en) && en.GetBoolean();
                var status = enabled ? "enabled" : "disabled";
                var displayName = prop.Value.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                var info = displayName.Length > 0 ? $"{prop.Name} ({displayName})" : prop.Name;
                context.Console.WriteLine($"{info} [{status}]");
            }
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
                context.Console.WriteLine($"Use --force to confirm deletion of agent '{name}'.");
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

            context.Console.WriteLine($"Agent '{name}' deleted.");
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
                context.Console.WriteLine($"Agent '{name}' not found in config.");
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
                context.Console.WriteLine($"Agent '{name}' not found in config.");
                context.ExitCode = 1;
                return;
            }

            // Use JsonElement deserialization
            var agents = DeserializeToDict(agentsEl);
            if (!agents.TryGetValue(name, out var agentEntry) || agentEntry is null)
            {
                context.Console.WriteLine($"Agent '{name}' not found in config.");
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

            context.Console.WriteLine($"Identity updated for '{name}'.");
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
            context.Console.WriteLine($"Bound '{name}' to {channel}.");
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
                context.Console.WriteLine("Specify --channel to remove a binding.");
                context.ExitCode = 1;
                return;
            }

            await ModifyBindingAsync(name, channel, add: false, context);
            context.Console.WriteLine($"Unbound '{name}' from {channel}.");
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
                context.Console.WriteLine($"Pairing approved. Sender added to {channel} allowlist.");
            else
            {
                context.Console.WriteLine("Invalid or expired pairing code.");
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
                context.Console.WriteLine("Policy must be: open, pairing, or allowlist.");
                context.ExitCode = 1;
                return;
            }

            var access = new ChannelAccess(channel!, _aetherDir, NullLogger<ChannelAccess>.Instance);
            await access.LoadAsync(context.GetCancellationToken());
            await access.SetModeAsync(policy, context.GetCancellationToken());

            context.Console.WriteLine($"Access policy for {channel} set to: {policy}");
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
            context.Console.WriteLine($"Keypair generated for '{agentName}'.");

            var agentConfig = AgentConfigForDir(agentDir);
            var bootConfig = agentConfig.Boot ?? new BootConfig();
            await signer.SignBootFilesAsync(bootConfig, context.GetCancellationToken());
            context.Console.WriteLine($"Boot files signed. Integrity directory: {Path.Combine(agentDir, "_INTEGRITY")}");
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
                context.Console.WriteLine($"Signed: {file}");
            else
            {
                context.Console.WriteLine($"Failed: {result.Error}");
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
                context.Console.WriteLine(result.Status switch
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
                    context.Console.WriteLine("All signed files verified.");
                else
                {
                    foreach (var (f, r) in failures)
                        context.Console.WriteLine($"✗ {f} — {r.Error}");
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
            // Try systemd service first
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "systemctl",
                Arguments = "is-active aether.service",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            try
            {
                using var check = System.Diagnostics.Process.Start(psi);
                if (check is not null)
                {
                    var output = (await check.StandardOutput.ReadToEndAsync()).Trim();
                    await check.WaitForExitAsync(context.GetCancellationToken());

                    if (output == "active")
                    {
                        context.Console.WriteLine("Restarting Aether service via systemd...");
                        var restart = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "sudo",
                            Arguments = "systemctl restart aether.service",
                            UseShellExecute = false
                        });
                        await restart!.WaitForExitAsync(context.GetCancellationToken());
                        context.Console.WriteLine(restart.ExitCode == 0
                            ? "Aether service restarted."
                            : $"systemctl restart failed with exit code {restart.ExitCode}");
                        return;
                    }
                }
            }
            catch
            {
                // systemctl not available, fall through
            }

            context.Console.WriteLine("Aether is not running as a systemd service.");
            context.Console.WriteLine("Install: sudo bash scripts/install-service.sh install");
            context.Console.WriteLine("Or start manually: dotnet run -- serve");
            context.ExitCode = 1;
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
        return new AgentConfig
        {
            Boot = new BootConfig
            {
                ConstitutionFiles = new() { "AGENTS_GUARD.md" },
                IdentityFiles = new() { "SOUL.md", "USER.md", "IDENTITY.md" }
            }
        };
    }

    private async Task ListBindingsAsync(string agentName, System.CommandLine.Invocation.InvocationContext context)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (!File.Exists(configPath))
        {
            context.Console.WriteLine("No bindings configured.");
            return;
        }

        var json = await File.ReadAllTextAsync(configPath, context.GetCancellationToken());
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("agents", out var agentsEl) ||
            !agentsEl.TryGetProperty(agentName, out var agent) ||
            !agent.TryGetProperty("bindings", out var bindings))
        {
            context.Console.WriteLine("No bindings configured.");
            return;
        }

        foreach (var binding in bindings.EnumerateArray())
            context.Console.WriteLine(binding.GetString() ?? "");
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
            context.Console.WriteLine($"Agent '{agentName}' not found. Add it first with 'aether agent add'.");
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
