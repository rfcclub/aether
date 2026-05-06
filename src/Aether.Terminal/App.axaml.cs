using Aether.Agent;
using Aether.Agents;
using Aether.Config;
using Aether.Data;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Terminal;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ConfigureServices();
            var vm = services.GetRequiredService<TerminalViewModel>();
            desktop.MainWindow = new MainWindow(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("AETHER_")
            .Build();

        services.AddSingleton<IConfiguration>(config);

        // Aether home directory
        var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME")
                         ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");

        // Database
        services.AddSingleton(provider =>
        {
            var cfg = provider.GetRequiredService<IConfiguration>();
            var databasePath = cfg["database:path"] ?? Path.Combine(aetherHome, "store", "aether.db");
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "Data", "Schema.sql");
            if (!File.Exists(schemaPath))
                schemaPath = Path.Combine("Data", "Schema.sql");
            return new AetherDb(databasePath, schemaPath);
        });

        // Core services
        services.AddSingleton<SessionManager>();
        services.AddSingleton<FileMemory>(provider =>
        {
            var cfg = provider.GetRequiredService<IConfiguration>();
            var groupsPath = cfg["groups:path"] ?? "groups";
            Directory.CreateDirectory(groupsPath);
            return new FileMemory(groupsPath);
        });
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<Aether.Agent.ToolExecutor>();
        services.AddSingleton<SkillRegistry>();
        services.AddSingleton<SkillParser>();
        services.AddSingleton<SkillTrigger>();
        services.AddSingleton<SkillEvolution>();

        // Agent profile
        var agentCfg = new AgentConfig
        {
            StartupFiles = (config["agent:startup_files"] ?? "SOUL.md,USER.md,AGENTS.md")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            LongTermMemoryFile = config["agent:long_term_memory"] ?? "MEMORY.md",
            HeartbeatFile = config["agent:heartbeat_file"] ?? "HEARTBEAT.md",
            DailyMemoryDirectory = config["agent:daily_memory_dir"] ?? "memory",
            TaskInboxFile = config["agent:task_inbox"] ?? "TASK_INBOX.md",
            TaskReportFile = config["agent:task_report"] ?? "TASK_REPORT.md"
        };
        services.AddSingleton(agentCfg);
        services.AddSingleton<AgentProfile>(_ =>
        {
            var agentName = config["assistant:name"] ?? "Aether";
            var agentsRoot = config["agent:root"] ?? ".";
            return new AgentProfile(agentName, agentsRoot, agentCfg);
        });

        // ── Dynamic provider registration ──
        var loader = new ConfigLoader(config, aetherHome, NullLogger<ConfigLoader>.Instance);
        var bootstrapConfig = loader.LoadAsync().GetAwaiter().GetResult();

        if (bootstrapConfig.Providers.Count == 0)
        {
            // Fallback to a single provider from legacy config
            var baseUrl = config["llm:base_url"] ?? "https://openrouter.ai/api/v1";
            var apiKey = config["llm:api_key"] ?? "";
            var model = config["llm:model"] ?? "nvidia/nemotron-3-super-120b-a12b:free";
            var entry = new SpecProviderEntry
            {
                Type = "openai",
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                Model = model
            };
            bootstrapConfig.Providers["openrouter"] = entry;
        }

        foreach (var (name, entry) in bootstrapConfig.Providers)
        {
            var capturedName = name;
            var capturedEntry = entry;
            services.AddSingleton<ILLMProvider>(provider =>
            {
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("Aether.Providers");
                return ProviderFactory.Create(capturedEntry, capturedName, logger);
            });
        }

        // ProviderRouter wraps all providers
        services.AddSingleton(provider =>
        {
            var providers = provider.GetRequiredService<IEnumerable<ILLMProvider>>().ToList();
            var db = provider.GetRequiredService<AetherDb>();
            var logger = NullLogger<ProviderRouter>.Instance;
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

        // FEOFALLS Cognitive Architecture — loads constitution, identity, cognitive, working state
        services.AddSingleton<BootConfig>(_ => agentCfg.Boot ?? new BootConfig());
        services.AddSingleton<BootContract>(provider =>
        {
            var profile = provider.GetRequiredService<AgentProfile>();
            var bootConfig = provider.GetRequiredService<BootConfig>();
            return new BootContract(profile.AgentDirectory, bootConfig);
        });

        // AetherSoul using ProviderRouter + Boot contract
        services.AddSingleton<AetherSoul>(provider =>
        {
            var llm = provider.GetRequiredService<ProviderRouter>();
            var tools = provider.GetRequiredService<Aether.Agent.ToolExecutor>();
            var profile = provider.GetRequiredService<AgentProfile>();
            return new AetherSoul(llm, tools, profile);
        });

        services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);

        // TerminalViewModel
        services.AddSingleton<TerminalViewModel>(sp =>
        {
            var soul = sp.GetRequiredService<AetherSoul>();
            var agentName = config["assistant:name"] ?? "Aether";
            var model = config["llm:model"] ?? "unknown";
            var logger = sp.GetRequiredService<ILogger<TerminalViewModel>>();
            return new TerminalViewModel(soul, ".", agentName, model, logger);
        });

        return services.BuildServiceProvider();
    }
}
