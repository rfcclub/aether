using System.CommandLine;
using Aether;
using Aether.Agent;
using Aether.Agents;
using Aether.Channels;
using Aether.Cli;
using Aether.Config;
using Aether.Data;
using Aether.Memory;
using Aether.Plugins;
using Aether.Providers;
using Microsoft.Extensions.DependencyInjection;
using Aether.Routing;
using Aether.SelfImprovement;
using Aether.WorkingDirectory;
using Aether.Workspace;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Aether.Tooling.DynamicTool;
using Aether.Ui;
using Aether.Ui.Handlers;
using Aether.Ui.Renderers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Microsoft.Extensions.Logging;
using Aether.Scheduling;
using Aether.Mcp;
using ToolExecutor = Aether.Agent.ToolExecutor;

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
{
    return;
}

if (args.Contains("--debug-args", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(string.Join(Environment.NewLine, args.Select((arg, index) => $"{index}: {arg}")));
    return;
}

var traceStartup = args.Contains("--trace-startup", StringComparer.OrdinalIgnoreCase);
var prompt = GetOption(args, "--prompt");

// CLI dispatch: route management commands before harness/serve/tui
if (args.FirstOrDefault() is "agent" or "integrity" or "access" or "gateway" or "plugin" or "restart")
{
    await RunCliAsync(args);
    return;
}

// First-run wizard: create ~/.aether/config.json if missing
// Skip in harness mode — it doesn't need ~/.aether/
if (prompt is null)
{
    var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
    var aetherDir = aetherHome ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
    var wizard = new FirstRunWizard(aetherDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<FirstRunWizard>.Instance);
    if (wizard.IsFirstRun())
    {
        var interactive = !args.Contains("--non-interactive", StringComparer.OrdinalIgnoreCase);
        await wizard.RunAsync(interactive);
    }
}

if (args.FirstOrDefault() == "serve")
{
    await RunServeAsync(traceStartup);
    return;
}

if (prompt is not null)
{
    await RunPromptHarnessAsync(args, prompt, traceStartup);
    return;
}

// Default: onboard REPL
await RunOnboardReplAsync(args, traceStartup);

static async Task RunCliAsync(string[] args)
{
    var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
    var aetherDir = aetherHome ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");

    var scaffolder = new AgentWorkspaceScaffolder(
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentWorkspaceScaffolder>.Instance);
    var authProfiles = new AgentAuthProfiles(aetherDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentAuthProfiles>.Instance);
    var cli = new AetherCli(aetherDir, scaffolder, authProfiles,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AetherCli>.Instance);

    Environment.ExitCode = await cli.BuildRootCommand().InvokeAsync(args);
}

static async Task RunPromptHarnessAsync(string[] args, string prompt, bool traceStartup)
{
    void HarnessTrace(string message)
    {
        if (traceStartup)
        {
            Console.Error.WriteLine($"[prompt] {message}");
        }
    }

    HarnessTrace("before config");
    var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("AETHER_")
        .Build();
    HarnessTrace("after config");

    var timeoutSeconds = GetIntOption(args, "--timeout-seconds")
        ?? configuration.GetValue("llm:timeout_seconds", 90);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

    try
    {
        var group = GetOption(args, "--group") ?? "main";
        var model = GetOption(args, "--model")
            ?? ConfigValue(configuration, "llm:model", "nvidia/nemotron-3-super-120b-a12b:free");
        Console.Error.WriteLine($"Aether calling model '{model}' for group '{group}' with {timeoutSeconds}s timeout...");

        var databasePath = GetOption(args, "--database-path")
            ?? ConfigValue(configuration, "database:path", "store/aether.db");
        var schemaPath = ResolvePath(ConfigValue(configuration, "database:schema", Path.Combine("Data", "Schema.sql")));
        var db = new AetherDb(databasePath, schemaPath);
        HarnessTrace("before database init");
        await db.InitializeAsync(cts.Token);
        HarnessTrace("after database init");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(ConfigValue(configuration, "llm:base_url", "https://openrouter.ai/api/v1").TrimEnd('/') + "/")
        };
        var provider = new OpenRouterProvider(
            httpClient,
            new OpenRouterOptions(
                ApiKey: ReadApiKey(args, configuration),
                Model: model,
                BaseUrl: ConfigValue(configuration, "llm:base_url", "https://openrouter.ai/api/v1")));
        var memory = new FileMemory(ConfigValue(configuration, "groups:path", "groups"));
        var sessions = new SessionManager(db);
        var toolExecutor = new ToolExecutor(configuration);
        var profile = ResolveAgentProfile("aether", configuration);

        // ── Plugin system ──
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var pluginsPath = Path.GetFullPath(configuration["plugins:path"] ?? Path.Combine(aetherHome, "plugins"));
        var pluginLoader = new Aether.Plugins.PluginLoader(pluginsPath, Microsoft.Extensions.Logging.Abstractions.NullLogger<Aether.Plugins.PluginLoader>.Instance);
        var (pluginResult, manifestPairs) = await pluginLoader.LoadAllAsync(cts.Token);
        var hooks = new HookEngine(pluginResult.Hooks, Microsoft.Extensions.Logging.Abstractions.NullLogger<HookEngine>.Instance);
        
        // Filter hooks for agent
        var agentSpec = configuration.GetSection("assistant").Get<AgentSpecConfig>(); // Minimal mock for harness
        if (agentSpec?.Plugins is not null)
        {
            hooks = hooks.FilterForAgent(agentSpec.Plugins);
        }

        var sqliteMemory = new SqliteMemorySystem(databasePath, Path.Combine(profile.AgentDirectory, "MEMORY.md"),
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SqliteMemorySystem>());
        await sqliteMemory.InitializeAsync(cts.Token);
        
        var skillRegistry = new SkillRegistry(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SkillRegistry>());
        var soul = new AetherSoul(provider, toolExecutor, profile, hooks: hooks, sqliteMemory: sqliteMemory, sessionManager: sessions);

        var response = await soul.ProcessAsync(group, prompt, cts.Token);
        Console.WriteLine(response.Content);
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        Console.Error.WriteLine($"Aether prompt timed out after {timeoutSeconds}s.");
        Environment.ExitCode = 124;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
}

static string ResolveDefaultAgentName(IServiceProvider provider)
{
    var configLoader = provider.GetService<ConfigLoader>();
    if (configLoader is not null)
    {
        var result = configLoader.LoadAsync().Result;
        foreach (var (name, entry) in result.Agents)
        {
            if (entry.Enabled)
                return name;
        }
    }
    return "default";
}

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path))
    {
        return path;
    }

    var cwdPath = Path.GetFullPath(path);
    if (File.Exists(cwdPath))
    {
        return cwdPath;
    }

    return Path.Combine(AppContext.BaseDirectory, path);
}

static string ConfigValue(IConfiguration configuration, string key, string fallback)
{
    var envKey = "AETHER_" + key.Replace(':', '_');
    var doubleUnderscoreEnvKey = "AETHER_" + key.Replace(":", "__", StringComparison.Ordinal);
    var envValue = Environment.GetEnvironmentVariable(doubleUnderscoreEnvKey)
        ?? Environment.GetEnvironmentVariable(doubleUnderscoreEnvKey.ToUpperInvariant())
        ?? Environment.GetEnvironmentVariable(envKey)
        ?? Environment.GetEnvironmentVariable(envKey.ToUpperInvariant());
    if (!string.IsNullOrWhiteSpace(envValue))
    {
        return envValue;
    }

    var direct = configuration[key];
    if (!string.IsNullOrWhiteSpace(direct) && !direct.StartsWith("${", StringComparison.Ordinal))
    {
        return direct;
    }

    return fallback;
}

static string ReadApiKey(string[] args, IConfiguration configuration)
{
    var apiKeyFile = GetOption(args, "--api-key-file");
    if (!string.IsNullOrWhiteSpace(apiKeyFile))
    {
        return File.ReadAllText(apiKeyFile).Trim();
    }

    return ConfigValue(configuration, "llm:api_key", "");
}

static AgentProfile ResolveAgentProfile(string name, IConfiguration configuration)
{
    var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
    var aetherDir = aetherHome ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");

    var config = new AgentConfig { StartupFiles = new() };

    // Try ConfigLoader-backed resolution if ~/.aether/ exists
    if (Directory.Exists(aetherDir))
    {
        try
        {
            var authProfiles = new AgentAuthProfiles(aetherDir,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentAuthProfiles>.Instance);
            var configLoader = new ConfigLoader(configuration, aetherDir,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance, authProfiles);
            return AgentProfile.FromConfigLoader(name, configLoader, config);
        }
        catch (DirectoryNotFoundException)
        {
            // Fall through to current-directory fallback
        }
    }

    // Fallback: use current directory (repo-relative, no ~/.aether/ needed)
    return new AgentProfile(name, ".", config, new AgentModelConfig());
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return i + 1 < args.Length ? args[i + 1] : "";
    }

    return null;
}

static int? GetIntOption(string[] args, string name)
{
    var value = GetOption(args, name);
    if (value is null)
    {
        return null;
    }

    return int.TryParse(value, out var parsed) ? parsed : throw new ArgumentException($"{name} must be an integer.");
}

static async Task RunOnboardReplAsync(string[] args, bool traceStartup)
{
    var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("AETHER_")
        .Build();

    var group = GetOption(args, "--group") ?? "main";
    var model = GetOption(args, "--model")
        ?? ConfigValue(configuration, "llm:model", "nvidia/nemotron-3-super-120b-a12b:free");

    var databasePath = GetOption(args, "--database-path")
        ?? ConfigValue(configuration, "database:path", "store/aether.db");
    var schemaPath = ResolvePath(ConfigValue(configuration, "database:schema", Path.Combine("Data", "Schema.sql")));

    var db = new AetherDb(databasePath, schemaPath);
    await db.InitializeAsync(CancellationToken.None);

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(ConfigValue(configuration, "llm:base_url", "https://openrouter.ai/api/v1").TrimEnd('/') + "/")
    };
    var provider = new OpenRouterProvider(
        httpClient,
        new OpenRouterOptions(
            ApiKey: ReadApiKey(args, configuration),
            Model: model,
            BaseUrl: ConfigValue(configuration, "llm:base_url", "https://openrouter.ai/api/v1")));
    var memory = new FileMemory(ConfigValue(configuration, "groups:path", "groups"));
    var sessions = new SessionManager(db);
    var toolExecutor = new ToolExecutor(configuration);
    var skillRegistry = new SkillRegistry(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SkillRegistry>());
    var skillTrigger = new SkillTrigger(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SkillTrigger>());
    var profile = ResolveAgentProfile("aether", configuration);

    // ── Plugin system ──
    var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
    var pluginsPath = Path.Combine(aetherHome, "plugins");
    var pluginLoader = new Aether.Plugins.PluginLoader(pluginsPath, Microsoft.Extensions.Logging.Abstractions.NullLogger<Aether.Plugins.PluginLoader>.Instance);
    var (pluginResult, manifestPairs) = await pluginLoader.LoadAllAsync(CancellationToken.None);
    var hooks = new HookEngine(pluginResult.Hooks, Microsoft.Extensions.Logging.Abstractions.NullLogger<HookEngine>.Instance);

    // ── Initialize Lifecycles ──
    foreach (var lifecycle in pluginResult.LifecycleHandlers)
    {
        await lifecycle.OnLoadAsync(new PluginContext
        {
            PluginDirectory = pluginsPath,
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Services = new ServiceCollection().BuildServiceProvider(),
            Manifest = new PluginManifest { Name = "repl-plugin" }
        }, CancellationToken.None);
    }

    var sqliteMemory = new SqliteMemorySystem(databasePath, Path.Combine(profile.AgentDirectory, "MEMORY.md"),
        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SqliteMemorySystem>());
    await sqliteMemory.InitializeAsync(CancellationToken.None);

    var soul = new AetherSoul(provider, toolExecutor, profile, hooks: hooks, sqliteMemory: sqliteMemory, sessionManager: sessions);

    // ── OnSessionStart hook ──
    var startCtx = new OnSessionStartContext { AgentName = "aether", WorkspacePath = profile.AgentDirectory, IsNewSession = true };
    await hooks.RunAsync(HookPoint.OnSessionStart, startCtx, CancellationToken.None);

    AnsiConsole.Write(new Panel(
        new Markup($"[bold]Model:[/] [violet]{Markup.Escape(model)}[/]\n" +
                   $"[bold]Group:[/] [violet]{Markup.Escape(group)}[/]\n" +
                   "[dim]Type /help for commands, /quit /q /exit or Ctrl+C to exit[/]"))
        .Header("[bold amber]Aether — Onboard REPL[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Grey));
    AnsiConsole.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (!cts.Token.IsCancellationRequested)
    {
        AnsiConsole.Markup("[violet]>[/] ");
        var input = Console.ReadLine();
        if (input is null || cts.Token.IsCancellationRequested) break;

        var trimmed = input.Trim();
        if (trimmed is "/quit" or "/q" or "/exit") break;
        if (string.IsNullOrWhiteSpace(trimmed)) continue;

        if (trimmed is "/help" or "/h")
        {
            var helpTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Command[/]"))
                .AddColumn(new TableColumn("[bold]Description[/]"));
            helpTable.AddRow("[violet]/help[/]", "Show this help");
            helpTable.AddRow("[violet]/quit[/]", "Exit the REPL");
            helpTable.AddRow("[violet]/clear[/]", "Clear the screen");
            AnsiConsole.Write(helpTable);
            continue;
        }

        if (trimmed is "/clear")
        {
            AnsiConsole.Clear();
            continue;
        }

        // ── OnMessageReceived hook ──
        var msgCtx = new OnMessageReceivedContext { AgentName = "aether", WorkspacePath = profile.AgentDirectory, Text = trimmed, ChannelName = "repl" };
        var msgResult = await hooks.RunAsync(HookPoint.OnMessageReceived, msgCtx, cts.Token);
        if (!msgResult.Success || msgCtx.Dropped) continue;
        var processedInput = msgCtx.OverrideText ?? trimmed;

        AnsiConsole.WriteLine();
        try
        {
            var response = await soul.ProcessAsync(group, processedInput, cts.Token);
            var responseText = response.Content;

            // ── OnMessageSent hook ──
            var sentCtx = new OnMessageSentContext { AgentName = "aether", WorkspacePath = profile.AgentDirectory, Text = responseText };
            await hooks.RunAllAsync(HookPoint.OnMessageSent, sentCtx, cts.Token);
            responseText = sentCtx.OverrideText ?? responseText;

            AnsiConsole.Write(new Rule());
            AnsiConsole.MarkupLine(responseText);
            AnsiConsole.Write(new Rule());
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
        }
        AnsiConsole.WriteLine();
    }

    // ── OnSessionEnd hook ──
    var endCtx = new HookContext { AgentName = "aether", WorkspacePath = profile.AgentDirectory };
    await hooks.RunAsync(HookPoint.OnSessionEnd, endCtx, CancellationToken.None);

    AnsiConsole.MarkupLine("[dim]Aether signing off.[/]");
}

static async Task RunServeAsync(bool traceStartup)
{
    void Trace(string message)
    {
        if (traceStartup)
            Console.Error.WriteLine($"[startup] {message}");
    }

    Trace("before builder");
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        // WSL: disable default appsettings.json file probing (extremely slow on Plan 9 fs)
        // We add appsettings.json explicitly below with optional: true.
        DisableDefaults = true,
        ContentRootPath = Environment.CurrentDirectory,
        ApplicationName = "Aether"
    });
    Trace("after builder");

    var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
    var aetherConfigPath = Path.Combine(aetherHome, "config.json");

    builder.Configuration
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
        .AddJsonFile(aetherConfigPath, optional: true, reloadOnChange: true)
        .AddEnvironmentVariables("AETHER_");

    builder.Logging.AddSimpleConsole(c => c.IncludeScopes = false);
    builder.Logging.AddProvider(new FileLoggerProvider(
        Path.Combine(aetherHome, "logs", "aether.log")));
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    builder.Logging.AddFilter("Console", LogLevel.Warning);

    builder.Services.AddSingleton<AetherHostMarker>();
    builder.Services.AddSingleton(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var databasePath = configuration["database:path"] ?? "store/aether.db";
        var schemaPath = ResolvePath(configuration["database:schema"] ?? Path.Combine("Data", "Schema.sql"));
        return new AetherDb(databasePath, schemaPath);
    });
    builder.Services.AddSingleton<ChannelMessageQueue>();
    builder.Services.AddSingleton<MessageRouter>(provider =>
    {
        var configLoader = provider.GetRequiredService<ConfigLoader>();
        var logger = provider.GetRequiredService<ILogger<MessageRouter>>();
        return new MessageRouter(configLoader, logger);
    });
    builder.Services.AddSingleton<SessionManager>(provider =>
    {
        var db = provider.GetRequiredService<AetherDb>();
        var hooks = provider.GetService<Aether.Plugins.HookEngine>();
        return new SessionManager(db, hooks);
    });
    builder.Services.AddSingleton<FileMemory>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var groupsPath = configuration["groups:path"] ?? "groups";
        var hooks = provider.GetService<Aether.Plugins.HookEngine>();
        return new FileMemory(groupsPath, hooks);
    });
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<ToolExecutor>();
    builder.Services.AddSingleton<Aether.Tooling.ToolExecutor>();

    // Tool ecosystem — built-in tool implementations
    builder.Services.AddHttpClient<TavilyWebSearchProvider>();
    builder.Services.AddHttpClient<WebFetchTool>();
    builder.Services.AddSingleton<ReadTool>();
    builder.Services.AddSingleton<WriteTool>();
    builder.Services.AddSingleton<EditTool>();
    builder.Services.AddSingleton<GlobTool>();
    builder.Services.AddSingleton<GrepTool>();
    builder.Services.AddSingleton<BashTool>();
    builder.Services.AddSingleton<SkillListTool>();
    builder.Services.AddSingleton<SkillReadTool>();
    builder.Services.AddSingleton<MemoryReadTool>();
    builder.Services.AddSingleton<MemoryWriteTool>();
    builder.Services.AddSingleton<MemorySearchTool>();
    builder.Services.AddSingleton<SessionStatusTool>();
    builder.Services.AddSingleton<SessionResetTool>();
    builder.Services.AddSingleton<MkdirTool>();
    builder.Services.AddSingleton<DeleteFileTool>();
    builder.Services.AddSingleton<MoveFileTool>();
    builder.Services.AddSingleton<GitStatusTool>();
    builder.Services.AddSingleton<GitDiffTool>();
    builder.Services.AddSingleton<RunCommandTool>();
    builder.Services.AddSingleton<ApplyPatchTool>();

    // Register all IToolImplementation instances for tool-code bridge
    builder.Services.AddSingleton<IEnumerable<IToolImplementation>>(provider =>
    {
        return new IToolImplementation[]
        {
            provider.GetRequiredService<ReadTool>(),
            provider.GetRequiredService<WriteTool>(),
            provider.GetRequiredService<EditTool>(),
            provider.GetRequiredService<GlobTool>(),
            provider.GetRequiredService<GrepTool>(),
            provider.GetRequiredService<BashTool>(),
            provider.GetRequiredService<SkillListTool>(),
            provider.GetRequiredService<SkillReadTool>(),
            provider.GetRequiredService<MemoryReadTool>(),
            provider.GetRequiredService<MemoryWriteTool>(),
            provider.GetRequiredService<MemorySearchTool>(),
            provider.GetRequiredService<SessionStatusTool>(),
            provider.GetRequiredService<SessionResetTool>(),
            provider.GetRequiredService<MkdirTool>(),
            provider.GetRequiredService<DeleteFileTool>(),
            provider.GetRequiredService<MoveFileTool>(),
            provider.GetRequiredService<GitStatusTool>(),
            provider.GetRequiredService<GitDiffTool>(),
            provider.GetRequiredService<RunCommandTool>(),
            provider.GetRequiredService<ApplyPatchTool>(),
        };
    });

    // Register built-in tools at startup
    builder.Services.AddSingleton<IHostedService>(provider =>
    {
        var registry = provider.GetRequiredService<ToolRegistry>();
        var logger = provider.GetRequiredService<ILogger<ToolStartupRegistration>>();
        var webFetchTool = provider.GetRequiredService<WebFetchTool>();
        var impls = provider.GetRequiredService<IEnumerable<IToolImplementation>>();
        var searchProvider = provider.GetService<TavilyWebSearchProvider>();
        return new ToolStartupRegistration(registry, impls, webFetchTool, logger, searchProvider);
    });
    builder.Services.AddSingleton<SlashCommandHandler>();
    builder.Services.AddSingleton<ModelSelectionHandler>();
    builder.Services.AddSingleton<IUiCallbackHandler, ModelSelectionHandler>(p => p.GetRequiredService<ModelSelectionHandler>());
    builder.Services.AddSingleton<CallbackRouter>();
    builder.Services.AddSingleton<TelegramUiRenderer>();
    builder.Services.AddSingleton<WebSocketUiRenderer>();
    builder.Services.AddSingleton<SkillRegistry>();
    builder.Services.AddSingleton<SkillParser>();
    builder.Services.AddSingleton<SkillTrigger>();
    builder.Services.AddSingleton<SkillEvolution>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<SkillEvolution>>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var patchesPath = configuration["self_improvement:patches_path"] ?? "patches";
        var db = provider.GetService<AetherDb>();
        return new SkillEvolution(logger, patchesPath, db);
    });
    // Working directory: creates ~/.aether/ on first run before anything else
    builder.Services.AddSingleton(provider =>
    {
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
        var aetherDir = aetherHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var logger = provider.GetRequiredService<ILogger<WorkingDirectoryInitializer>>();
        return new WorkingDirectoryInitializer(aetherDir, logger);
    });
    builder.Services.AddSingleton<IHostedService>(provider =>
        provider.GetRequiredService<WorkingDirectoryInitializer>());

    // Configuration hierarchy
    builder.Services.AddSingleton<AgentAuthProfiles>(provider =>
    {
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
        var aetherDir = aetherHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var logger = provider.GetRequiredService<ILogger<AgentAuthProfiles>>();
        return new AgentAuthProfiles(aetherDir, logger);
    });

    builder.Services.AddSingleton<ConfigLoader>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
        var aetherDir = aetherHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var logger = provider.GetRequiredService<ILogger<ConfigLoader>>();
        var authProfiles = provider.GetRequiredService<AgentAuthProfiles>();
        return new ConfigLoader(configuration, aetherDir, logger, authProfiles);
    });

    // Agent workspace scaffolding
    builder.Services.AddSingleton<AgentWorkspaceScaffolder>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<AgentWorkspaceScaffolder>>();
        return new AgentWorkspaceScaffolder(logger);
    });

    builder.Services.AddSingleton<IHostedService, AetherInitializationService>();
    builder.Services.AddScoped<GoalStore>(provider =>
    {
        var db = provider.GetRequiredService<AetherDb>();
        return new GoalStore(db);
    });

    builder.Services.AddHostedService<ProactiveTaskService>();
    builder.Services.AddHostedService<PluginLifecycleService>();

    // Tool hot-reload: watches tools/*.json for changes at runtime.
    builder.Services.AddSingleton<IHostedService>(provider =>
    {
        var registry = provider.GetRequiredService<ToolRegistry>();
        var logger = provider.GetRequiredService<ILogger<ToolHotReloadService>>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var toolsPath = configuration["tooling:hot_reload_path"] ?? "tools";
        return new ToolHotReloadService(registry, logger, toolsPath);
    });

    // Self-improvement services
    builder.Services.AddSingleton<PipelineTracker>();
    builder.Services.AddSingleton<BenchmarkGate>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILogger<BenchmarkGate>>();
        var testProjectPath = configuration["self_improvement:test_project"] ?? "tests/Aether.Tests";
        var timeoutSeconds = configuration.GetValue("self_improvement:benchmark_timeout_seconds", 60);
        return new BenchmarkGate(testProjectPath, timeoutSeconds, logger);
    });
    builder.Services.AddSingleton<SelfImprovementService>(provider =>
    {
        var memory = provider.GetRequiredService<IMemorySystem>();
        var skillEvolution = provider.GetRequiredService<SkillEvolution>();
        var benchmarkGate = provider.GetRequiredService<BenchmarkGate>();
        var pipelineTracker = provider.GetRequiredService<PipelineTracker>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILogger<SelfImprovementService>>();
        var patchesPath = configuration["self_improvement:patches_path"] ?? "patches";
        return new SelfImprovementService(memory, skillEvolution, benchmarkGate, pipelineTracker, patchesPath, provider, logger);
    });
    builder.Services.AddHostedService<DailyReviewHostedService>();

    // ── Dynamic provider registration ──
    // Providers are loaded from config.json / appsettings.json via ConfigLoader.
    // Each provider gets its own HttpClient registered as an ILLMProvider singleton.
    var aetherHomeDir = Environment.GetEnvironmentVariable("AETHER_HOME");
    var aetherCfgDir = aetherHomeDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
    var bootstrapLoader = new ConfigLoader(
        builder.Configuration, aetherCfgDir,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
    var bootstrapConfig = await bootstrapLoader.LoadAsync();

    if (bootstrapConfig.Providers.Count == 0)
    {
        Console.Error.WriteLine("Aether: No providers configured. Add providers to ~/.aether/config.json or appsettings.json.");
    }

    foreach (var (name, entry) in bootstrapConfig.Providers)
    {
        var providers = ProviderFactory.CreateAll(entry, name, null);
        foreach (var p in providers)
        {
            var captured = p;
            builder.Services.AddSingleton<ILLMProvider>(_ => captured);
        }
    }

    builder.Services.AddSingleton<ProviderHealthMonitor>();
    builder.Services.AddSingleton(provider =>
    {
        var providers = provider.GetRequiredService<IEnumerable<ILLMProvider>>().ToList();
        var db = provider.GetRequiredService<AetherDb>();
        var logger = provider.GetRequiredService<ILogger<ProviderRouter>>();
        var config = provider.GetRequiredService<IConfiguration>();
        var priorities = new Dictionary<string, int>();
        var prioritiesList = config.GetSection("provider_priorities").Get<string[]>();
        if (prioritiesList is { Length: > 0 })
        {
            for (var i = 0; i < prioritiesList.Length; i++)
                priorities[prioritiesList[i]] = i + 1;
        }
        var options = new ProviderRoutingOptions { ProviderPriorities = priorities };
        return new ProviderRouter(providers, options, db, logger);
    });

    // Agent Profile System
    builder.Services.AddSingleton<AgentConfig>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var agentName = configuration["agent:name"] ?? ResolveDefaultAgentName(provider);
        var agentsRoot = configuration["agent:root"] ?? "agents";
        var agentDir = Path.Combine(agentsRoot, agentName);

        return new AgentConfig
        {
            StartupFiles = (configuration["agent:startup_files"] ?? "AGENTS.md")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            LongTermMemoryFile = configuration["agent:long_term_memory"] ?? "MEMORY.md",
            HeartbeatFile = configuration["agent:heartbeat_file"] ?? "HEARTBEAT.md",
            DailyMemoryDirectory = configuration["agent:daily_memory_dir"] ?? "memory",
            TaskInboxFile = configuration["agent:task_inbox"] ?? "TASK_INBOX.md",
            TaskReportFile = configuration["agent:task_report"] ?? "TASK_REPORT.md"
        };
    });

    builder.Services.AddSingleton<AgentProfile>(provider =>
    {
        var config = provider.GetRequiredService<AgentConfig>();
        var configLoader = provider.GetRequiredService<ConfigLoader>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var agentName = configuration["agent:name"] ?? ResolveDefaultAgentName(provider);
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<AgentProfile>();
        return AgentProfile.FromConfigLoader(agentName, configLoader, config, logger);
    });

    builder.Services.AddSingleton<AgentMemoryBridge>(provider =>
    {
        var profile = provider.GetRequiredService<AgentProfile>();
        var config = provider.GetRequiredService<AgentConfig>();
        return new AgentMemoryBridge(profile.AgentDirectory, config);
    });

    builder.Services.AddHostedService(provider =>
    {
        var profile = provider.GetRequiredService<AgentProfile>();
        var soul = provider.GetRequiredService<AetherSoul>();
        var config = provider.GetRequiredService<AgentConfig>();
        var logger = provider.GetRequiredService<ILogger<AgentHeartbeatService>>();
        var hooks = provider.GetService<Aether.Plugins.HookEngine>();
        return new AgentHeartbeatService(profile, soul, config, logger, hooks: hooks);
    });

    // Cron scheduler — recurring tasks from ~/.aether/cron/*.md
    builder.Services.AddHostedService(provider =>
    {
        var soul = provider.GetRequiredService<AetherSoul>();
        var channel = provider.GetRequiredService<IChannel>();
        var logger = provider.GetRequiredService<ILogger<CronSchedulerService>>();
        var cronDir = Path.Combine(aetherCfgDir, "cron");
        return new CronSchedulerService(cronDir, soul, channel, logger);
    });

    // KAIROS proactive notifier — file-watch notifications
    builder.Services.AddHostedService(provider =>
    {
        var profile = provider.GetRequiredService<AgentProfile>();
        var cfgLoader = provider.GetRequiredService<ConfigLoader>();
        var cfg = provider.GetRequiredService<IConfiguration>();
        var agent = cfg["assistant:name"] ?? "default";
        var agentSpec = cfgLoader.GetAgentSpec(agent);
        var kairosCfg = agentSpec?.Kairos ?? new SpecKairosSection();
        var kairosConfig = new KairosConfig(
            kairosCfg.Enabled,
            kairosCfg.Rules.Select(r => new KairosRule(r.Watch, r.Channel, r.CooldownSeconds)).ToList());
        var channel = provider.GetRequiredService<IChannel>();
        var logger = provider.GetRequiredService<ILogger<KairosWatchService>>();
        return new KairosWatchService(profile.AgentDirectory, kairosConfig, channel, logger);
    });

    // Dynamic Tool Runtime — Roslyn hot-reload for tools/*.cs
    builder.Services.AddSingleton<DynamicToolWatcherService>(provider =>
    {
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var toolsDir = Path.Combine(aetherHome, "tools");
        var logger = provider.GetRequiredService<ILogger<DynamicToolWatcherService>>();
        return new DynamicToolWatcherService(toolsDir, logger);
    });
    builder.Services.AddSingleton<IHostedService>(provider =>
        provider.GetRequiredService<DynamicToolWatcherService>());
    builder.Services.AddSingleton<DynamicToolExecutor>(provider =>
    {
        var watcher = provider.GetRequiredService<DynamicToolWatcherService>();
        var logger = provider.GetRequiredService<ILogger<DynamicToolExecutor>>();
        return new DynamicToolExecutor(watcher, logger);
    });

    // MCP Server — Model Context Protocol stdio endpoint
    builder.Services.AddSingleton<McpServerEndpoint>(provider =>
    {
        var registry = provider.GetRequiredService<ToolRegistry>();
        var logger = provider.GetRequiredService<ILogger<McpServerEndpoint>>();
        return new McpServerEndpoint(registry, logger);
    });
    builder.Services.AddSingleton<IHostedService>(provider =>
        provider.GetRequiredService<McpServerEndpoint>());

    // Boot Cognitive Architecture
    builder.Services.AddSingleton<BootConfig>(provider =>
    {
        var config = provider.GetRequiredService<AgentConfig>();
        return config.Boot ?? new BootConfig();
    });

    builder.Services.AddSingleton<IntegritySigner>(provider =>
    {
        var profile = provider.GetRequiredService<AgentProfile>();
        var logger = provider.GetRequiredService<ILogger<IntegritySigner>>();
        return new IntegritySigner(profile.AgentDirectory, logger);
    });

    builder.Services.AddSingleton<EpisodicLogger>(provider =>
    {
        var profile = provider.GetRequiredService<AgentProfile>();
        var bootConfig = provider.GetRequiredService<BootConfig>();
        return new EpisodicLogger(profile.AgentDirectory, bootConfig);
    });

    builder.Services.AddSingleton<LifecycleStateMachine>(provider =>
    {
        var bootConfig = provider.GetRequiredService<BootConfig>();
        return new LifecycleStateMachine(bootConfig);
    });

    builder.Services.AddSingleton<ContextAssembler>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var tokenBudget = configuration.GetValue("agent:dynamic_token_budget", 4000);
        return new ContextAssembler(tokenBudget);
    });

    // ── Plugin system ──
    builder.Services.AddSingleton<Aether.Plugins.PluginLoader>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var pluginsPath = Path.GetFullPath(configuration["plugins:path"] ?? Path.Combine(aetherHome, "plugins"));
        var logger = provider.GetRequiredService<ILogger<Aether.Plugins.PluginLoader>>();
        return new Aether.Plugins.PluginLoader(pluginsPath, logger);
    });

    builder.Services.AddSingleton<Aether.Plugins.HookEngine>(provider =>
    {
        var pluginLoader = provider.GetRequiredService<Aether.Plugins.PluginLoader>();
        var (pluginResult, manifestPairs) = pluginLoader.LoadAllAsync().GetAwaiter().GetResult();
        var diHooks = provider.GetServices<IHook>();
        var allHooks = diHooks.Concat(pluginResult.Hooks).ToList();

        // Register plugin assets (tools, skills, cron) into registries
        var toolRegistry = provider.GetRequiredService<ToolRegistry>();
        var skillRegistry = provider.GetRequiredService<SkillRegistry>();
        var cronScheduler = provider.GetService<CronSchedulerService>();
        var assetLogger = provider.GetRequiredService<ILogger<Aether.Plugins.PluginAssetRegistrar>>();
        var registrar = new Aether.Plugins.PluginAssetRegistrar(toolRegistry, skillRegistry, cronScheduler, assetLogger);
        registrar.RegisterAsync(pluginResult, manifestPairs, CancellationToken.None).GetAwaiter().GetResult();

        var logger = provider.GetRequiredService<ILogger<Aether.Plugins.HookEngine>>();
        return new Aether.Plugins.HookEngine(allHooks, logger);
    });

    builder.Services.AddSingleton<SqliteMemorySystem>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var profile = provider.GetRequiredService<AgentProfile>();
        var databasePath = configuration["database:path"] ?? "store/aether.db";
        var memoryFilePath = Path.Combine(profile.AgentDirectory, "MEMORY.md");
        var logger = provider.GetRequiredService<ILogger<SqliteMemorySystem>>();
        var hooks = provider.GetService<Aether.Plugins.HookEngine>();
        return new SqliteMemorySystem(databasePath, memoryFilePath, logger, hooks);
    });

    builder.Services.AddTransient<AetherSoul>(provider =>
    {
        var llm = provider.GetRequiredService<ProviderRouter>();
        var tools = provider.GetRequiredService<Aether.Tooling.ToolExecutor>();
        var registry = provider.GetRequiredService<ToolRegistry>();
        var profile = provider.GetRequiredService<AgentProfile>();
        var sqliteMemory = provider.GetRequiredService<SqliteMemorySystem>();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var logger = provider.GetRequiredService<ILogger<AetherSoul>>();
        var hooks = provider.GetService<Aether.Plugins.HookEngine>();
        var configLoader = provider.GetService<Aether.Config.ConfigLoader>();
        var configuration = provider.GetService<IConfiguration>();
        var dynamicTools = provider.GetService<DynamicToolExecutor>();
        return new AetherSoul(llm, tools, registry, profile, logger, hooks, sqliteMemory, sessionManager, configLoader, configuration, dynamicTools);
    });

    builder.Services.AddSingleton<IChannel>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var telegramEnabled = configuration.GetValue("channels:telegram:enabled", false);
        var botToken = configuration["channels:telegram:bot_token"] ?? "";

        if (telegramEnabled && !string.IsNullOrWhiteSpace(botToken))
        {
            var logger = provider.GetRequiredService<ILogger<TelegramChannel>>();
            return new TelegramChannel(botToken, logger);
        }

        return new NoOpChannel();
    });

    // WebSocket channel: registered separately (not as IChannel) since it runs alongside Telegram
    // rather than replacing it. WebSocketChannelService manages its lifecycle as a BackgroundService.
    builder.Services.AddSingleton<WebSocketChannel>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var port = configuration.GetValue("channels:websocket:port", 5099);
        var logger = provider.GetRequiredService<ILogger<WebSocketChannel>>();
        return new WebSocketChannel(
            port,
            logger,
            provider.GetRequiredService<ProviderRouter>(),
            provider.GetRequiredService<SlashCommandHandler>(),
            provider.GetRequiredService<SessionManager>(),
            provider);
    });
    builder.Services.AddHostedService<WebSocketChannelService>();

    builder.Services.AddSingleton<ChannelAccess>(provider =>
    {
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
        var aetherDir = aetherHome ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
        var logger = provider.GetRequiredService<ILogger<ChannelAccess>>();
        return new ChannelAccess("telegram", aetherDir, logger);
    });

    builder.Services.AddSingleton<SessionCompactionService>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<SessionCompactionService>());

    builder.Services.AddHostedService<ChannelMessageProcessor>();

    Trace("before host build");
    using var host = builder.Build();
    Trace("after host build");

    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aether");
    logger.LogInformation("Aether host initialized.");

    // Initialize cryptographic identity and sign boot files
    // Set AETHER_SKIP_INTEGRITY=1 to skip (e.g., first boot before data is copied)
    if (Environment.GetEnvironmentVariable("AETHER_SKIP_INTEGRITY") == "1")
    {
        logger.LogInformation("Integrity check skipped (AETHER_SKIP_INTEGRITY=1)");
    }
    else
    {
        try
        {
            var integritySigner = host.Services.GetRequiredService<IntegritySigner>();
            var bootConfig = host.Services.GetRequiredService<BootConfig>();
            var publicKey = await integritySigner.InitializeAsync();
            logger.LogInformation("Agent identity key initialized.");

            var failures = await integritySigner.VerifyAllAsync();
            if (failures.Count > 0)
            {
                foreach (var (file, result) in failures)
                {
                    if (result.Status == IntegrityStatus.Unsigned)
                        logger.LogInformation("Integrity: {File} is unsigned — will sign.", file);
                    else
                        logger.LogInformation("Integrity: {File} modified — re-signing ({Error}).", file, result.Error);
                }
#pragma warning disable CS0618
                await integritySigner.SignBootFilesAsync(bootConfig);
                logger.LogInformation("Boot files re-signed ({Count} files).",
                    bootConfig.ConstitutionFiles.Count + bootConfig.IdentityFiles.Count);
            }
            else
            {
                await integritySigner.SignBootFilesAsync(bootConfig);
                logger.LogInformation("Boot file integrity verified ({Count} signed, 0 failures).",
                    bootConfig.ConstitutionFiles.Count + bootConfig.IdentityFiles.Count);
            }
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Integrity initialization skipped — agent will run without cryptographic identity");
        }
    }

    // ── OnAgentStart hook ──
    var hooks = host.Services.GetService<Aether.Plugins.HookEngine>();
    if (hooks is not null)
    {
        var profile = host.Services.GetRequiredService<AgentProfile>();
        var isFirstBoot = !Directory.Exists(Path.Combine(profile.AgentDirectory, "memory"));
        await hooks.RunAllAsync(HookPoint.OnAgentStart, new OnAgentStartContext
        {
            AgentName = profile.Name,
            WorkspacePath = profile.AgentDirectory,
            IsFirstBoot = isFirstBoot,
            AgentVersion = "3.0.0"
        }, CancellationToken.None);
    }

    // ── OnAgentStop hook on shutdown ──
    var shutdownCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        if (hooks is not null)
        {
            var profile = host.Services.GetRequiredService<AgentProfile>();
            hooks.RunAllAsync(HookPoint.OnAgentStop, new HookContext
            {
                AgentName = profile.Name,
                WorkspacePath = profile.AgentDirectory
            }, CancellationToken.None).GetAwaiter().GetResult();
        }
        shutdownCts.Cancel();
    };

    try
    {
        await host.RunAsync(shutdownCts.Token);
    }
    catch (OperationCanceledException) { }
}

internal sealed class AetherHostMarker;
