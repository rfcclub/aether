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
using Aether.Routing;
using Aether.SelfImprovement;
using Aether.WorkingDirectory;
using Aether.Workspace;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToolExecutor = Aether.Agent.ToolExecutor;
using IToolExecutor = Aether.Agent.IToolExecutor;

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

// CLI dispatch: route to agent management commands before harness/serve/tui
if (args.FirstOrDefault() == "agent")
{
    await RunCliAsync(args);
    return;
}

// First-run wizard: create ~/.aether/config.json if missing
var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME");
var aetherDir = aetherHome ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
var wizard = new FirstRunWizard(aetherDir,
    Microsoft.Extensions.Logging.Abstractions.NullLogger<FirstRunWizard>.Instance);
if (wizard.IsFirstRun())
{
    await wizard.RunNonInteractiveAsync();
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
        var profile = new AgentProfile("aether", ".", new AgentConfig { StartupFiles = new() });
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
    var profile = new AgentProfile("aether", ".", new AgentConfig { StartupFiles = new() });
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
    var builder = Host.CreateApplicationBuilder();
    Trace("after builder");

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("AETHER_");

    builder.Services.AddSingleton<AetherHostMarker>();
    builder.Services.AddSingleton(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var databasePath = configuration["database:path"] ?? "store/aether.db";
        var schemaPath = ResolvePath(configuration["database:schema"] ?? Path.Combine("Data", "Schema.sql"));
        return new AetherDb(databasePath, schemaPath);
    });
    builder.Services.AddSingleton<IMessageQueue, ChannelMessageQueue>();
    builder.Services.AddSingleton<MessageRouter>();
    builder.Services.AddSingleton<ISessionManager, SessionManager>();
    builder.Services.AddSingleton<IMemorySystem>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var groupsPath = configuration["groups:path"] ?? "groups";
        return new FileMemory(groupsPath);
    });
    builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();
    builder.Services.AddSingleton<IToolExecutor, ToolExecutor>();
    builder.Services.AddSingleton<ISkillRegistry, SkillRegistry>();
    builder.Services.AddSingleton<ISkillLoader, SkillParser>();
    builder.Services.AddSingleton<ISkillTrigger, SkillTrigger>();
    builder.Services.AddSingleton<ISkillEvolution, SkillEvolution>(provider =>
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
        var registry = provider.GetRequiredService<IToolRegistry>();
        var logger = provider.GetRequiredService<ILogger<ToolHotReloadService>>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var toolsPath = configuration["tooling:hot_reload_path"] ?? "tools";
        return new ToolHotReloadService(registry, logger, toolsPath);
    });

    // Self-improvement services
    builder.Services.AddSingleton<IPipelineTracker, PipelineTracker>();
    builder.Services.AddSingleton<IBenchmarkGate>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILogger<BenchmarkGate>>();
        var testProjectPath = configuration["self_improvement:test_project"] ?? "tests/Aether.Tests";
        var timeoutSeconds = configuration.GetValue("self_improvement:benchmark_timeout_seconds", 60);
        return new BenchmarkGate(testProjectPath, timeoutSeconds, logger);
    });
    builder.Services.AddSingleton<ISelfImprovementService>(provider =>
    {
        var memory = provider.GetRequiredService<IMemorySystem>();
        var skillEvolution = provider.GetRequiredService<ISkillEvolution>();
        var benchmarkGate = provider.GetRequiredService<IBenchmarkGate>();
        var pipelineTracker = provider.GetRequiredService<IPipelineTracker>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILogger<SelfImprovementService>>();
        var patchesPath = configuration["self_improvement:patches_path"] ?? "patches";
        return new SelfImprovementService(memory, skillEvolution, benchmarkGate, pipelineTracker, patchesPath, logger);
    });
    builder.Services.AddHostedService<DailyReviewHostedService>();

    builder.Services.AddHttpClient<ILLMProvider, OpenRouterProvider>((provider, client) =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["llm:base_url"] ?? "https://openrouter.ai/api/v1";
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    });
    builder.Services.AddHttpClient<ILLMProvider, FireworksProvider>((provider, client) =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["fireworks:base_url"] ?? "https://api.fireworks.ai/inference/v1";
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    });
    builder.Services.AddHttpClient<ILLMProvider, AnthropicProvider>((provider, client) =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["anthropic:base_url"] ?? "https://api.anthropic.com";
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    });

    builder.Services.AddSingleton(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        return new OpenRouterOptions(
            ApiKey: configuration["llm:api_key"] ?? "",
            Model: configuration["llm:model"] ?? "nvidia/nemotron-3-super-120b-a12b:free",
            BaseUrl: configuration["llm:base_url"] ?? "https://openrouter.ai/api/v1");
    });
    builder.Services.AddSingleton(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        return new FireworksOptions(
            ApiKey: configuration["fireworks:api_key"] ?? "",
            Model: configuration["fireworks:model"] ?? "accounts/fireworks/models/deepseek-v3-0324",
            BaseUrl: configuration["fireworks:base_url"] ?? "https://api.fireworks.ai/inference/v1");
    });
    builder.Services.AddSingleton(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        return new AnthropicOptions(
            ApiKey: configuration["anthropic:api_key"] ?? "",
            Model: configuration["anthropic:model"] ?? "claude-3-5-sonnet-20241022",
            BaseUrl: configuration["anthropic:base_url"] ?? "https://api.anthropic.com");
    });

    builder.Services.AddSingleton<ProviderHealthMonitor>();
    builder.Services.AddSingleton(provider =>
    {
        var providers = provider.GetRequiredService<IEnumerable<ILLMProvider>>().ToList();
        var db = provider.GetRequiredService<AetherDb>();
        var logger = provider.GetRequiredService<ILogger<ProviderRouter>>();
        var options = new ProviderRoutingOptions
        {
            ProviderPriorities = new Dictionary<string, int>
            {
                ["fireworks"] = 1,
                ["openrouter"] = 2,
                ["anthropic"] = 3
            }
        };
        return new ProviderRouter(providers, options, db, logger);
    });

    // Agent Profile System
    builder.Services.AddSingleton<AgentConfig>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var agentName = configuration["agent:name"] ?? "maria";
        var agentsRoot = configuration["agent:root"] ?? "agents";
        var agentDir = Path.Combine(agentsRoot, agentName);

        return new AgentConfig
        {
            StartupFiles = (configuration["agent:startup_files"] ?? "SOUL.md,USER.md")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            LongTermMemoryFile = configuration["agent:long_term_memory"] ?? "MEMORY.md",
            HeartbeatFile = configuration["agent:heartbeat_file"] ?? "HEARTBEAT.md",
            DailyMemoryDirectory = configuration["agent:daily_memory_dir"] ?? "memory",
            TaskInboxFile = configuration["agent:task_inbox"] ?? "TASK_INBOX.md",
            TaskReportFile = configuration["agent:task_report"] ?? "TASK_REPORT.md"
        };
    });

    builder.Services.AddSingleton<IAgentProfile>(provider =>
    {
        var config = provider.GetRequiredService<AgentConfig>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var agentName = configuration["agent:name"] ?? "maria";
        var agentsRoot = configuration["agent:root"] ?? "agents";
        var agentDir = Path.Combine(agentsRoot, agentName);

        return new AgentProfile(agentName, agentDir, config);
    });

    builder.Services.AddSingleton<AgentMemoryBridge>(provider =>
    {
        var profile = provider.GetRequiredService<IAgentProfile>();
        var config = provider.GetRequiredService<AgentConfig>();
        return new AgentMemoryBridge(profile.AgentDirectory, config);
    });

    builder.Services.AddHostedService<AgentHeartbeatService>();

    // FEOFALLS Cognitive Architecture
    builder.Services.AddSingleton<FeofallsConfig>(provider =>
    {
        var config = provider.GetRequiredService<AgentConfig>();
        return config.Feofalls ?? new FeofallsConfig();
    });

    builder.Services.AddSingleton<IBootContract>(provider =>
    {
        var profile = provider.GetRequiredService<IAgentProfile>();
        var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
        return new FeofallsBootContract(profile.AgentDirectory, feofallsConfig);
    });

    builder.Services.AddSingleton<EpisodicLogger>(provider =>
    {
        var profile = provider.GetRequiredService<IAgentProfile>();
        var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
        return new EpisodicLogger(profile.AgentDirectory, feofallsConfig);
    });

    builder.Services.AddSingleton<LifecycleStateMachine>(provider =>
    {
        var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
        return new LifecycleStateMachine(feofallsConfig);
    });

    builder.Services.AddSingleton<WriteValidator>(provider =>
    {
        var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
        return new WriteValidator(feofallsConfig);
    });

    builder.Services.AddSingleton<AetherSoul>(provider =>
    {
        var llm = provider.GetRequiredService<ILLMProvider>();
        var memory = provider.GetRequiredService<IMemorySystem>();
        var tools = provider.GetRequiredService<IToolExecutor>();
        var sessions = provider.GetRequiredService<ISessionManager>();
        var skills = provider.GetRequiredService<ISkillRegistry>();
        var skillTrigger = provider.GetRequiredService<ISkillTrigger>();
        var profile = provider.GetRequiredService<IAgentProfile>();
        var bootContract = provider.GetRequiredService<IBootContract>();
        return new AetherSoul(llm, memory, tools, sessions, skills, skillTrigger, profile, bootContract);
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

    builder.Services.AddHostedService<ChannelMessageProcessor>();

    Trace("before host build");
    using var host = builder.Build();
    Trace("after host build");

    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aether");
    logger.LogInformation("Aether host initialized.");

    await host.RunAsync();
}

internal sealed class AetherHostMarker;
