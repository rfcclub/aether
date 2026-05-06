using System.CommandLine;
using Aether;
using Aether.Agent;
using Aether.Agents;
using Aether.Channels;
using Aether.Cli;
using Aether.Config;
using Aether.Data;
using Aether.Memory;
using Aether.Providers;
using Microsoft.Extensions.DependencyInjection;
using Aether.Routing;
using Aether.SelfImprovement;
using Aether.WorkingDirectory;
using Aether.Workspace;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Aether.Scheduling;
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
if (args.FirstOrDefault() is "agent" or "integrity" or "access")
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

if (args.FirstOrDefault() == "tui")
{
    await LaunchTuiAsync(args.Skip(1).ToArray());
    return;
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
        var skillRegistry = new SkillRegistry(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SkillRegistry>());
        var skillTrigger = new SkillTrigger(LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SkillTrigger>());
        var profile = ResolveAgentProfile("aether", configuration);
        var soul = new AetherSoul(provider, memory, toolExecutor, sessions, skillRegistry, skillTrigger, profile);

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
    return new AgentProfile(name, ".", config);
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
    var soul = new AetherSoul(provider, memory, toolExecutor, sessions, skillRegistry, skillTrigger, profile);

    Console.WriteLine("╔══════════════════════════════════════╗");
    Console.WriteLine("║         Aether — Onboard REPL       ║");
    Console.WriteLine("╠══════════════════════════════════════╣");
    Console.WriteLine($"║ Model:    {model,-27}║");
    Console.WriteLine($"║ Group:    {group,-27}║");
    Console.WriteLine("║ Type /quit, /q, or Ctrl+C to exit   ║");
    Console.WriteLine("╚══════════════════════════════════════╝");
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (!cts.Token.IsCancellationRequested)
    {
        Console.Write("> ");
        var input = Console.ReadLine();
        if (input is null || cts.Token.IsCancellationRequested) break;

        var trimmed = input.Trim();
        if (trimmed is "/quit" or "/q" or "/exit") break;
        if (string.IsNullOrWhiteSpace(trimmed)) continue;

        Console.WriteLine();
        try
        {
            var response = await soul.ProcessAsync(group, trimmed, cts.Token);
            Console.WriteLine(response.Content);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Error: {ex.Message}]");
        }
        Console.WriteLine();
    }

    Console.WriteLine("Aether signing off.");
}

static async Task LaunchTuiAsync(string[] args)
{
    // Find TUI project path relative to the Aether project
    var tuiProjectDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Aether.Tui");
    tuiProjectDir = Path.GetFullPath(tuiProjectDir);

    if (!File.Exists(Path.Combine(tuiProjectDir, "Aether.Tui.csproj")))
    {
        // Try relative to current directory
        tuiProjectDir = Path.Combine(Environment.CurrentDirectory, "..", "Aether.Tui");
        tuiProjectDir = Path.GetFullPath(tuiProjectDir);
    }

    if (!File.Exists(Path.Combine(tuiProjectDir, "Aether.Tui.csproj")))
    {
        Console.Error.WriteLine("Cannot find Aether.Tui project. Run: dotnet run --project src/Aether.Tui");
        Environment.ExitCode = 1;
        return;
    }

    var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"run --project \"{tuiProjectDir}\" -- {string.Join(" ", args)}")
    {
        UseShellExecute = false,
        RedirectStandardInput = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false
    };

    using var process = System.Diagnostics.Process.Start(psi);
    if (process is not null)
    {
        await process.WaitForExitAsync();
        Environment.ExitCode = process.ExitCode;
    }
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
    builder.Services.AddSingleton<SessionManager>();
    builder.Services.AddSingleton<FileMemory>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var groupsPath = configuration["groups:path"] ?? "groups";
        return new FileMemory(groupsPath);
    });
    builder.Services.AddSingleton<ToolRegistry>();
    builder.Services.AddSingleton<ToolExecutor>();

    // Tool ecosystem — built-in tool implementations
    builder.Services.AddHttpClient<TavilyWebSearchProvider>();
    builder.Services.AddSingleton<TavilyWebSearchProvider>(provider =>
        provider.GetRequiredService<TavilyWebSearchProvider>());
    builder.Services.AddHttpClient<WebFetchTool>();
    builder.Services.AddSingleton<ReadTool>();
    builder.Services.AddSingleton<WriteTool>();
    builder.Services.AddSingleton<EditTool>();
    builder.Services.AddSingleton<GlobTool>();
    builder.Services.AddSingleton<GrepTool>();
    builder.Services.AddSingleton<BashTool>();

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
        };
    });

    // Register built-in tools at startup
    builder.Services.AddSingleton<IHostedService>(provider =>
    {
        var registry = provider.GetRequiredService<ToolRegistry>();
        var logger = provider.GetRequiredService<ILogger<ToolStartupRegistration>>();
        var webFetchTool = provider.GetRequiredService<WebFetchTool>();
        var impls = provider.GetRequiredService<IEnumerable<IToolImplementation>>();
        return new ToolStartupRegistration(registry, impls, webFetchTool, logger);
    });
    builder.Services.AddSingleton<SlashCommandHandler>();
    builder.Services.AddSingleton<SkillRegistry>();
    builder.Services.AddSingleton<SkillParser>();
    builder.Services.AddSingleton<SkillTrigger>();
    builder.Services.AddSingleton<SkillEvolution>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<SkillEvolution>>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var patchesPath = configuration["self_improvement:patches_path"] ?? "patches";
        return new SkillEvolution(logger, patchesPath);
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
        var memory = provider.GetRequiredService<FileMemory>();
        var skillEvolution = provider.GetRequiredService<SkillEvolution>();
        var benchmarkGate = provider.GetRequiredService<BenchmarkGate>();
        var pipelineTracker = provider.GetRequiredService<PipelineTracker>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILogger<SelfImprovementService>>();
        var patchesPath = configuration["self_improvement:patches_path"] ?? "patches";
        return new SelfImprovementService(memory, skillEvolution, benchmarkGate, pipelineTracker, patchesPath, logger);
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
    var bootstrapConfig = bootstrapLoader.LoadAsync().Result;

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

    builder.Services.AddHostedService<AgentHeartbeatService>();

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

    // Boot Cognitive Architecture
    builder.Services.AddSingleton<BootConfig>(provider =>
    {
        var config = provider.GetRequiredService<AgentConfig>();
        return config.Boot ?? new BootConfig();
    });

    builder.Services.AddSingleton<BootContract>(provider =>
    {
        var profile = provider.GetRequiredService<AgentProfile>();
        var bootConfig = provider.GetRequiredService<BootConfig>();
        return new BootContract(profile.AgentDirectory, bootConfig);
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

    builder.Services.AddSingleton<WriteValidator>(provider =>
    {
        var bootConfig = provider.GetRequiredService<BootConfig>();
        return new WriteValidator(bootConfig);
    });

    builder.Services.AddSingleton<ContextAssembler>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var tokenBudget = configuration.GetValue("agent:dynamic_token_budget", 4000);
        return new ContextAssembler(tokenBudget);
    });

    builder.Services.AddSingleton<AetherSoul>(provider =>
    {
        var llm = provider.GetRequiredService<ProviderRouter>();
        var memory = provider.GetRequiredService<FileMemory>();
        var tools = provider.GetRequiredService<ToolExecutor>();
        var sessions = provider.GetRequiredService<SessionManager>();
        var skills = provider.GetRequiredService<SkillRegistry>();
        var skillTrigger = provider.GetRequiredService<SkillTrigger>();
        var profile = provider.GetRequiredService<AgentProfile>();
        var bootContract = provider.GetRequiredService<BootContract>();
        var contextAssembler = provider.GetRequiredService<ContextAssembler>();
        return new AetherSoul(llm, memory, tools, sessions, skills, skillTrigger, profile, bootContract, contextAssembler);
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
        return new WebSocketChannel(port, logger);
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Integrity initialization skipped — agent will run without cryptographic identity");
        }
    }

    await host.RunAsync();
}

internal sealed class AetherHostMarker;
