using Aether;
using Aether.Agent;
using Aether.Agents;
using Aether.Channels;
using Aether.Config;
using Aether.Data;
using Aether.Memory;
using Aether.Plugins;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Aether.Tui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Gui;

// ═══════════════════════════════════════════════════════════════════════════════
//  AETHER TUI — Ethereal Theme
// ═══════════════════════════════════════════════════════════════════════════════

// ── Theme Colors ──────────────────────────────────────────────────────────────
var BG          = Color.Black;
var USER_TEXT   = Color.Gray;
var AGENT_TEXT  = Color.White;
var ACCENT      = Color.Cyan;
var DIM         = Color.DarkGray;
var INPUT_BG    = Color.Black;
var HEADER_FG   = Color.BrightCyan;

// ── Bootstrap ─────────────────────────────────────────────────────────────────
var configuration = LoadConfiguration(args);
var services = BuildServices(configuration);
var provider = services.GetRequiredService<IServiceProvider>();

var cts = new CancellationTokenSource();
AgentProfile profile;
try
{
    profile = provider.GetRequiredService<AgentProfile>();
}
catch (Exception)
{
    Console.ForegroundColor = ConsoleColor.Red;
    var requestedAgent = configuration["agent:name"] ?? "default";
    Console.Error.WriteLine($"Agent '{requestedAgent}' is not configured or enabled");
    Console.ResetColor();
    Environment.Exit(1);
    return;
}
var channel = (TuiChannel)provider.GetRequiredService<IChannel>();

// Initialize Terminal.Gui first so MainLoop is ready
Application.Init();
var top = Application.Top;

// ── Color Schemes ─────────────────────────────────────────────────────────────
var schemeDefault = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(USER_TEXT, BG),
    HotNormal = new Terminal.Gui.Attribute(DIM, BG),
    Focus     = new Terminal.Gui.Attribute(AGENT_TEXT, BG),
    HotFocus  = new Terminal.Gui.Attribute(ACCENT, BG),
};

var schemeChat = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(USER_TEXT, BG),
    HotNormal = new Terminal.Gui.Attribute(AGENT_TEXT, BG),
    Focus     = new Terminal.Gui.Attribute(USER_TEXT, BG),
    HotFocus  = new Terminal.Gui.Attribute(AGENT_TEXT, BG),
};

var schemeHeader = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(HEADER_FG, BG),
    HotNormal = new Terminal.Gui.Attribute(ACCENT, BG),
    Focus     = new Terminal.Gui.Attribute(HEADER_FG, BG),
    HotFocus  = new Terminal.Gui.Attribute(ACCENT, BG),
};

var schemeInput = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(AGENT_TEXT, INPUT_BG),
    HotNormal = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
    Focus     = new Terminal.Gui.Attribute(AGENT_TEXT, INPUT_BG),
    HotFocus  = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
};

var schemeStatus = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(DIM, BG),
    HotNormal = new Terminal.Gui.Attribute(ACCENT, BG),
    Focus     = new Terminal.Gui.Attribute(DIM, BG),
    HotFocus  = new Terminal.Gui.Attribute(ACCENT, BG),
};

top.ColorScheme = schemeDefault;

// ── Scroll state ──────────────────────────────────────────────────────────────
var userScrolledUp = false;
var newMessagesAvailable = false;
var waitingDots = 0;
Timer? waitingTimer = null;
bool isStreamingStarted = false;

// ══════════════════════════════════════════════════════════════════════════════
//  LAYOUT:  header (1) | chat (fill) | separator (1) | input (3) | status (1)
// ══════════════════════════════════════════════════════════════════════════════

var mainLayout = new View
{
    X = 0, Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ColorScheme = schemeDefault,
};

// ── Header:  ─ Aether · Maria ─────────────────────── model ──────
var headerLabel = new Label($" Aether \u00b7 {profile.DisplayName}")
{
    X = 0, Y = 0,
    Width = Dim.Fill(),
    Height = 1,
    ColorScheme = schemeHeader,
};

// ── Chat area ────────────────────────────────────────────────────────────────
var chatView = new TextView
{
    X = 0, Y = 1,
    Width = Dim.Fill(),
    Height = Dim.Fill(5),  // leave room for separator + input + status
    WordWrap = true,
    CanFocus = false,
    ColorScheme = schemeChat,
    BottomOffset = 0,
    RightOffset = 0,
};

// ── Separator ─────────────────────────────────────────────────────────────────
var separator = new Label(new string('\u2500', 200))
{
    X = 0, Y = Pos.Bottom(chatView),
    Width = Dim.Fill(),
    Height = 1,
    ColorScheme = schemeStatus,
};

// ── Input area:  │ > _                                                      │
var inputField = new TextField("")
{
    X = 2, Y = Pos.Bottom(separator) + 1,
    Width = Dim.Fill(2),
    Height = 1,
    ColorScheme = schemeInput,
};

var schemeInputPrompt = new ColorScheme
{
    Normal    = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
    HotNormal = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
    Focus     = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
    HotFocus  = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
};

var inputPrompt = new Label(" \u276f ")
{
    X = 0, Y = Pos.Bottom(separator) + 1,
    Width = 2,
    Height = 1,
    ColorScheme = schemeInputPrompt,
};

// ── Status bar:  ● Connected \u00b7 Agent: Maria \u00b7 Ctrl+Q Quit ──────────────────
var statusLabel = new Label(" \u25cf Aether \u00b7 Ready")
{
    X = 0, Y = Pos.Bottom(separator) + 3,
    Width = Dim.Fill(),
    Height = 1,
    ColorScheme = schemeStatus,
};

// ── Assemble ──────────────────────────────────────────────────────────────────
mainLayout.Add(headerLabel);
mainLayout.Add(chatView);
mainLayout.Add(separator);
mainLayout.Add(inputPrompt);
mainLayout.Add(inputField);
mainLayout.Add(statusLabel);

top.Add(mainLayout);

// ── Wire TuiChannel callbacks (thread-safe via MainLoop.Invoke) ───────────────
channel.SetCallbacks(
    text => Application.MainLoop.Invoke(() =>
    {
        // Agent message — reformat with timestamp
        var ts = DateTime.Now.ToString("HH:mm");
        var lines = text.Split(Environment.NewLine);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0)
                AppendChat($"  {ts}  Aether  \u2502  {lines[i]}");
            else
                AppendChat($"         \u2502  {lines[i]}");
        }
    }),
    chunk => Application.MainLoop.Invoke(() => AppendStreamingChunk(chunk)),
    () => Application.MainLoop.Invoke(() => CompleteStreaming())
);

// Now start background hosted services (ChannelMessageProcessor, AetherInitializationService, etc.)
var hostedServices = provider.GetServices<IHostedService>();
_ = Task.Run(async () =>
{
    try
    {
        foreach (var hs in hostedServices)
            await hs.StartAsync(cts.Token).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        System.IO.File.WriteAllText("tui_error.log", ex.ToString());
    }
});


// ══════════════════════════════════════════════════════════════════════════════
//  HELPERS
// ══════════════════════════════════════════════════════════════════════════════

static int GetTotalLines(TextView view) => view.Lines;
static int GetVisibleLines(TextView view) => Math.Max(1, view.Frame.Height - 2);

void UpdateScrollIndicator()
{
    var total = GetTotalLines(chatView);
    var topRow = chatView.TopRow;
    var visible = GetVisibleLines(chatView);
    var bottom = Math.Min(topRow + visible, total);

    var indicator = newMessagesAvailable ? " \u25bc new" : "";
    var scrollInfo = total > 0 ? $"  [{topRow + 1}-{bottom}/{total}{indicator}]" : "";

    statusLabel.Text = $" \u25cf Aether \u00b7 {profile.DisplayName}{scrollInfo}";
    Application.Refresh();
}

void AppendChat(string text)
{
    chatView.Text += text + Environment.NewLine;

    if (!userScrolledUp)
    {
        ScrollToBottom();
    }
    else
    {
        newMessagesAvailable = true;
        UpdateScrollIndicator();
    }
}

void AppendStreamingChunk(string chunk)
{
    if (string.IsNullOrEmpty(chunk)) return;

    var ts = DateTime.Now.ToString("HH:mm");
    var textToAppend = "";

    if (!isStreamingStarted)
    {
        isStreamingStarted = true;
        textToAppend += $"  {ts}  Aether  \u2502  ";
    }

    // Handle newlines within the chunk for alignment
    var lines = chunk.Split('\n');
    for (int i = 0; i < lines.Length; i++)
    {
        if (i > 0)
        {
            textToAppend += Environment.NewLine + "         \u2502  ";
        }
        textToAppend += lines[i];
    }

    chatView.Text += textToAppend;

    if (!userScrolledUp)
    {
        var total = GetTotalLines(chatView);
        var visible = GetVisibleLines(chatView);
        chatView.ScrollTo(Math.Max(0, total - visible), true);
    }
    else
    {
        newMessagesAvailable = true;
    }
}

void CompleteStreaming()
{
    chatView.Text += Environment.NewLine;
    isStreamingStarted = false;
    StopWaiting();

    if (!userScrolledUp)
    {
        ScrollToBottom();
    }
    else
    {
        UpdateScrollIndicator();
    }
}

void StartWaiting()
{
    waitingDots = 0;
    waitingTimer?.Dispose();
    waitingTimer = new Timer(_ =>
    {
        waitingDots = (waitingDots + 1) % 4;
        var dots = new string('.', waitingDots);
        Application.MainLoop.Invoke(() =>
        {
            statusLabel.Text = $" \u25cf Aether \u00b7 Thinking{dots}   [Esc] Cancel";
            Application.Refresh();
        });
    }, null, 0, 400);
}

void StopWaiting()
{
    waitingTimer?.Dispose();
    waitingTimer = null;
    UpdateScrollIndicator();
}

void ScrollUp(int lines)
{
    var target = Math.Max(0, chatView.TopRow - lines);
    chatView.ScrollTo(target, true);
    userScrolledUp = chatView.TopRow < GetTotalLines(chatView) - 1;
    UpdateScrollIndicator();
}

void ScrollDown(int lines)
{
    var total = GetTotalLines(chatView);
    var visible = GetVisibleLines(chatView);
    var maxTop = Math.Max(0, total - visible);
    var target = Math.Min(maxTop, chatView.TopRow + lines);
    chatView.ScrollTo(target, true);

    if (chatView.TopRow >= maxTop)
    {
        userScrolledUp = false;
        newMessagesAvailable = false;
    }
    UpdateScrollIndicator();
}

void ScrollToBottom()
{
    var total = GetTotalLines(chatView);
    var visible = GetVisibleLines(chatView);
    chatView.ScrollTo(Math.Max(0, total - visible), true);
    userScrolledUp = false;
    newMessagesAvailable = false;
    UpdateScrollIndicator();
}

void SendMessage()
{
    var message = inputField.Text?.ToString()?.Trim();
    if (string.IsNullOrWhiteSpace(message)) return;

    inputField.Text = "";

    // Timestamp
    var ts = DateTime.Now.ToString("HH:mm");

    // User message
    var userName = configuration["user:name"];
    if (string.IsNullOrEmpty(userName) || userName.Equals("default", StringComparison.OrdinalIgnoreCase))
    {
        userName = "Thoor";
    }
    AppendChat($"  {ts}  {userName}  \u2502  {message}");

    // Start thinking indicator
    StartWaiting();

    // Agent response prefix (will be filled by streaming)
    AppendChat("");

    // Route to ChannelMessageProcessor
    channel.ReceiveUserInput(message);
}

// ══════════════════════════════════════════════════════════════════════════════
//  EVENT HANDLERS
// ══════════════════════════════════════════════════════════════════════════════

inputField.KeyPress += e =>
{
    if (e.KeyEvent.Key == Key.Enter)
    {
        e.Handled = true;
        SendMessage();
    }
};

Application.RootKeyEvent += keyEvent =>
{
    // Only handle scroll keys — let normal input pass through
    switch (keyEvent.Key)
    {
        case Key.PageUp:
            ScrollUp(GetVisibleLines(chatView));
            return true;
        case Key.PageDown:
            ScrollDown(GetVisibleLines(chatView));
            return true;
        case Key.End when !inputField.HasFocus:
            ScrollToBottom();
            return true;
        case Key.Esc:
            if (waitingTimer != null) { StopWaiting(); return true; }
            ScrollToBottom();
            return true;
    }
    return false;
};

Application.RootMouseEvent += mouseEvent =>
{
    if (mouseEvent.Flags.HasFlag(MouseFlags.WheeledUp))
    {
        ScrollUp(3);
        mouseEvent.Handled = true;
    }
    else if (mouseEvent.Flags.HasFlag(MouseFlags.WheeledDown))
    {
        ScrollDown(3);
        mouseEvent.Handled = true;
    }
};



// ══════════════════════════════════════════════════════════════════════════════
//  INITIAL STATE & RUN
// ══════════════════════════════════════════════════════════════════════════════

chatView.Text = "";

// Welcome message
AppendChat("");
AppendChat("  \u2728  Aether TUI \u2014 Ethereal Interface");
AppendChat($"  \u2502  Connected to agent: {profile.DisplayName}");
AppendChat($"  \u2502  Workspace: {profile.AgentDirectory}");
AppendChat("  \u2502");
AppendChat("  \u2502  Type a message to begin. PgUp/PgDn to scroll.");
AppendChat("");

inputField.SetFocus();
UpdateScrollIndicator();
Application.Run();

// ── Cleanup ───────────────────────────────────────────────────────────────────
waitingTimer?.Dispose();
cts.Cancel();
cts.Dispose();
Application.Shutdown();

// ══════════════════════════════════════════════════════════════════════════════
//  CONFIGURATION & DI
// ══════════════════════════════════════════════════════════════════════════════

static IConfiguration LoadConfiguration(string[] args)
{
    var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
    var globalConfigPath = Path.Combine(aetherHome, "config.json");

    var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    var builder = new ConfigurationBuilder()
        .AddJsonFile(appSettingsPath, optional: true, reloadOnChange: false)
        .AddJsonFile(globalConfigPath, optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("AETHER_");

    var agentName = TuiArgs.ParseAgentName(args);
    if (!string.IsNullOrEmpty(agentName))
    {
        builder.AddInMemoryCollection(new[]
        {
            new KeyValuePair<string, string?>("agent:name", agentName)
        });
    }

    return builder.Build();
}

static IServiceProvider BuildServices(IConfiguration configuration)
{
    var services = new ServiceCollection();

    services.AddSingleton<IConfiguration>(configuration);

    services.AddSingleton(provider =>
    {
        var config = provider.GetRequiredService<IConfiguration>();
        var databasePath = ResolvePath(config["database:path"] ?? "store/aether.db");
        var schemaPath = ResolvePath(config["database:schema"] ?? Path.Combine("Data", "Schema.sql"));
        return new AetherDb(databasePath, schemaPath);
    });

    services.AddSingleton<SessionManager, SessionManager>();
    services.AddSingleton<FileMemory>(provider =>
    {
        var config = provider.GetRequiredService<IConfiguration>();
        var groupsPath = ResolvePath(config["groups:path"] ?? "groups");
        Directory.CreateDirectory(groupsPath);
        return new FileMemory(groupsPath);
    });

    // Core Agent Services
    services.AddSingleton<ToolRegistry, ToolRegistry>();
    services.AddSingleton<Aether.Tooling.ToolExecutor, Aether.Tooling.ToolExecutor>();
    services.AddSingleton<SkillRegistry, SkillRegistry>();
    services.AddSingleton<SkillParser, SkillParser>();
    services.AddSingleton<SkillTrigger, SkillTrigger>();
    services.AddSingleton<SkillEvolution, SkillEvolution>();

    // Dynamic provider registration matching Core Server
    var aetherHomeDir = Environment.GetEnvironmentVariable("AETHER_HOME");
    var aetherCfgDir = aetherHomeDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
    var bootstrapLoader = new ConfigLoader(
        configuration, aetherCfgDir,
        NullLogger<ConfigLoader>.Instance);
    var bootstrapConfig = bootstrapLoader.LoadAsync().Result;

    foreach (var (name, entry) in bootstrapConfig.Providers)
    {
        var providers = ProviderFactory.CreateAll(entry, name, null);
        foreach (var p in providers)
        {
            var captured = p;
            services.AddSingleton<ILLMProvider>(_ => captured);
        }
    }

    if (bootstrapConfig.Providers.Count == 0)
    {
        services.AddSingleton<ILLMProvider>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var baseUrl = config["providers:openrouter:base_url"]
                          ?? config["llm:base_url"]
                          ?? "https://openrouter.ai/api/v1";
            var apiKey = config["providers:openrouter:api_key"] ?? config["llm:api_key"] ?? "";
            var model = config["providers:openrouter:model"] ?? config["llm:model"] ?? "nvidia/nemotron-3-super-120b-a12b:free";
            var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
            return new OpenRouterProvider(client, new OpenRouterOptions(apiKey, model, baseUrl));
        });
    }

    services.AddSingleton<AgentConfig>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
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

    services.AddSingleton<AgentProfile>(provider =>
    {
        var config = provider.GetRequiredService<AgentConfig>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var configLoader = provider.GetRequiredService<ConfigLoader>();
        var logger = provider.GetRequiredService<ILogger<AgentProfile>>();

        // Ensure config is loaded
        var appConfig = configLoader.LoadAsync().Result;

        var agentName = configuration["agent:name"];
        if (string.IsNullOrEmpty(agentName))
        {
            foreach (var (name, entry) in appConfig.Agents)
            {
                if (entry.Enabled)
                {
                    agentName = name;
                    break;
                }
            }
            agentName ??= "default";
        }

        return AgentProfile.FromConfigLoader(agentName, configLoader, config, logger);
    });

    services.AddSingleton<AetherSoul>(provider =>
    {
        var llm = provider.GetRequiredService<ProviderRouter>();
        var tools = provider.GetRequiredService<Aether.Tooling.ToolExecutor>();
        var registry = provider.GetRequiredService<ToolRegistry>();
        var profile = provider.GetRequiredService<AgentProfile>();
        var logger = provider.GetRequiredService<ILogger<AetherSoul>>();
        var sqliteMemory = provider.GetRequiredService<SqliteMemorySystem>();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        return new AetherSoul(llm, tools, registry, profile, logger, null, sqliteMemory, sessionManager);
    });

    services.AddSingleton<SqliteMemorySystem>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var profile = provider.GetRequiredService<AgentProfile>();
        var databasePath = ResolvePath(configuration["database:path"] ?? "store/aether.db");
        var memoryFilePath = Path.Combine(profile.AgentDirectory, "MEMORY.md");
        var logger = provider.GetRequiredService<ILogger<SqliteMemorySystem>>();
        return new SqliteMemorySystem(databasePath, memoryFilePath, logger, null);
    });

    // Channel Integration Services
    services.AddSingleton<ConfigLoader>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<ConfigLoader>>();
        return new ConfigLoader(configuration, aetherCfgDir, logger, null);
    });

    services.AddSingleton<ChannelAccess>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<ChannelAccess>>();
        return new ChannelAccess("tui", GetRepositoryRoot(), logger);
    });

    services.AddSingleton<ChannelMessageQueue>();
    services.AddSingleton<MessageRouter>(provider =>
    {
        var configLoader = provider.GetRequiredService<ConfigLoader>();
        var logger = provider.GetRequiredService<ILogger<MessageRouter>>();
        return new MessageRouter(configLoader, logger);
    });
    services.AddSingleton<SlashCommandHandler>();

    // Read provider_priorities from config (same as server)
    var priorities = new Dictionary<string, int>();
    var prioritiesList = configuration.GetSection("provider_priorities").Get<string[]>();
    if (prioritiesList is { Length: > 0 })
    {
        for (var i = 0; i < prioritiesList.Length; i++)
            priorities[prioritiesList[i]] = i + 1;
    }
    services.AddSingleton<ProviderRoutingOptions>(new ProviderRoutingOptions { ProviderPriorities = priorities });

    services.AddSingleton<IReadOnlyList<ILLMProvider>>(provider =>
        provider.GetServices<ILLMProvider>().ToList());

    services.AddSingleton<ProviderRouter>();

    // The TUI Channel
    services.AddSingleton<IChannel>(provider =>
    {
        return new TuiChannel(text => {}, chunk => {}, () => {});
    });

    // Core processor & Initializer
    services.AddHostedService<ChannelMessageProcessor>();
    services.AddHostedService<AetherInitializationService>();

    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

    return services.BuildServiceProvider();
}

static string GetRepositoryRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "Aether.sln")))
        {
            return dir;
        }
        dir = Path.GetDirectoryName(dir);
    }
    return AppContext.BaseDirectory;
}

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path)) return path;

    var repoRoot = GetRepositoryRoot();
    var repoPath = Path.GetFullPath(Path.Combine(repoRoot, path));
    if (File.Exists(repoPath) || Directory.Exists(repoPath)) return repoPath;

    var cwdPath = Path.GetFullPath(path);
    if (File.Exists(cwdPath)) return cwdPath;

    return Path.Combine(AppContext.BaseDirectory, path);
}
