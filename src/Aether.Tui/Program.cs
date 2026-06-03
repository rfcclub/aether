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

var configuration = LoadConfiguration();
var services = BuildServices(configuration);
var provider = services.GetRequiredService<IServiceProvider>();

var cts = new CancellationTokenSource();
var profile = provider.GetRequiredService<AgentProfile>();
var channel = (TuiChannel)provider.GetRequiredService<IChannel>();

// Start background services (ChannelMessageProcessor, etc.)
var hostedServices = provider.GetServices<IHostedService>();
foreach (var hs in hostedServices)
{
    await hs.StartAsync(cts.Token);
}

Application.Init();

var top = Application.Top;

// Athanor Fiery Theme
var athanorScheme = new ColorScheme
{
    Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
    HotNormal = new Terminal.Gui.Attribute(Color.BrightRed, Color.Black),
    Focus = new Terminal.Gui.Attribute(Color.White, Color.DarkGray),
    HotFocus = new Terminal.Gui.Attribute(Color.BrightRed, Color.DarkGray)
};
top.ColorScheme = athanorScheme;

// ---- Scrollback state ----
var userScrolledUp = false;
var newMessagesAvailable = false;

// ---- Build UI ----
var win = new Window($"Aether [{profile.Name}]")
{
    X = 0, Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ColorScheme = athanorScheme
};

var chatPane = new FrameView($"Athanor Forge (Workspace: {profile.AgentDirectory})")
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(3)
};

var chatView = new TextView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ReadOnly = true,
    WordWrap = true,
    CanFocus = true,
    ColorScheme = athanorScheme
};
chatPane.Add(chatView);

var inputFrame = new FrameView(null)
{
    X = 0,
    Y = Pos.Bottom(chatPane),
    Width = Dim.Fill(),
    Height = 3,
    ColorScheme = athanorScheme
};

var inputField = new TextField("")
{
    X = 1,
    Y = 0,
    Width = Dim.Fill(10),
    Height = 1
};

var sendButton = new Button("Forge")
{
    X = Pos.Right(inputField) + 1,
    Y = 0
};

var statusLabel = new Label("Ready")
{
    X = 1,
    Y = 1,
    Width = Dim.Fill()
};

inputFrame.Add(inputField);
inputFrame.Add(sendButton);
inputFrame.Add(statusLabel);

win.Add(chatPane);
win.Add(inputFrame);

// ---- StatusBar ----
var sbAgentItem = new StatusItem(Key.Null, $"Agent: {profile.Name}", null);
var sbWrapItem = new StatusItem(Key.Null, "Wrap: ON", null);
var sbScrollItem = new StatusItem(Key.Null, "", null);
var sbQuitItem = new StatusItem(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop());
var sbWrapToggleItem = new StatusItem(Key.Null, "~Ctrl+W~ Wrap", null);

var statusBar = new StatusBar(
    new[] { sbAgentItem, sbWrapItem, sbScrollItem, sbQuitItem, sbWrapToggleItem })
{
    ColorScheme = athanorScheme
};

top.Add(win);
top.Add(statusBar);

// ---- Helpers ----

static int GetTotalLines(TextView view) => view.Lines;

static int GetVisibleLines(TextView view) => Math.Max(1, view.Frame.Height - 2);

void UpdateScrollIndicator()
{
    var total = GetTotalLines(chatView);
    var topRow = chatView.TopRow;
    var visible = GetVisibleLines(chatView);
    var bottomRow = Math.Min(topRow + visible, total);

    if (newMessagesAvailable)
    {
        sbScrollItem.Title = $"L{topRow + 1}-{bottomRow}/{total}  ↓ New";
    }
    else
    {
        sbScrollItem.Title = total > 0 ? $"L{topRow + 1}-{bottomRow}/{total}" : "";
    }
    Application.Refresh();
}

void UpdateStatusBar()
{
    sbWrapItem.Title = chatView.WordWrap ? "Wrap: ON" : "Wrap: OFF";
    UpdateScrollIndicator();
    Application.Refresh();
}

void AppendChat(string text)
{
    chatView.Text += text + Environment.NewLine;

    if (!userScrolledUp)
    {
        var total = GetTotalLines(chatView);
        chatView.ScrollTo(total - 1, true);
        newMessagesAvailable = false;
    }
    else
    {
        newMessagesAvailable = true;
    }

    UpdateStatusBar();
}

void AppendStreamingChunk(string chunk)
{
    chatView.Text += chunk;

    if (!userScrolledUp)
    {
        var total = GetTotalLines(chatView);
        chatView.ScrollTo(total - 1, true);
    }
    else
    {
        newMessagesAvailable = true;
    }
}

void CompleteStreaming()
{
    chatView.Text += Environment.NewLine;
    statusLabel.Text = "Ready";
    if (!userScrolledUp)
    {
        var total = GetTotalLines(chatView);
        chatView.ScrollTo(total - 1, true);
    }
    UpdateStatusBar();
}

void ScrollUp(int lines)
{
    var target = Math.Max(0, chatView.TopRow - lines);
    chatView.ScrollTo(target, true);
    userScrolledUp = chatView.TopRow < GetTotalLines(chatView) - 1;
    UpdateStatusBar();
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
    UpdateStatusBar();
}

void ScrollToBottom()
{
    var total = GetTotalLines(chatView);
    chatView.ScrollTo(total - 1, true);
    userScrolledUp = false;
    newMessagesAvailable = false;
    UpdateStatusBar();
}

void SendMessage()
{
    var message = inputField.Text?.ToString()?.Trim();
    if (string.IsNullOrWhiteSpace(message)) return;

    inputField.Text = "";
    AppendChat($"Thoor> {message}");
    statusLabel.Text = "Forging...";
    
    // Output prefix for the agent before streaming begins
    chatView.Text += $"{profile.Name}> ";
    UpdateStatusBar();
    Application.Refresh();

    // Delegate routing and processing entirely to the ChannelMessageProcessor!
    channel.ReceiveUserInput(message);
}

void ToggleWordWrap()
{
    chatView.WordWrap = !chatView.WordWrap;
    UpdateStatusBar();
    Application.Refresh();
}

// ---- Event handlers ----

sendButton.Clicked += () => SendMessage();

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
    switch (keyEvent.Key)
    {
        case Key.W when keyEvent.IsCtrl:
            ToggleWordWrap();
            return true;
        case Key.PageUp:
            ScrollUp(GetVisibleLines(chatView));
            return true;
        case Key.PageDown:
            ScrollDown(GetVisibleLines(chatView));
            return true;
        case Key.End:
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

// ---- Connect Channel Callbacks to UI ----

channel = (Aether.Tui.TuiChannel)provider.GetRequiredService<IChannel>();
channel.GetType().GetProperty("OnMessageReceived")?.SetValue(channel, (Action<string>)(text => Application.MainLoop.Invoke(() => AppendChat(text))));
channel.GetType().GetProperty("OnStreamingChunk")?.SetValue(channel, (Action<string>)(chunk => Application.MainLoop.Invoke(() => { AppendStreamingChunk(chunk); })));
channel.GetType().GetProperty("OnStreamingComplete")?.SetValue(channel, (Action)(() => Application.MainLoop.Invoke(() => { CompleteStreaming(); })));

// ---- Initial state ----
chatView.Text = $"Athanor TUI ready. Connected to Agent: {profile.Name}{Environment.NewLine}";
UpdateStatusBar();

Application.Run();

// ---- Cleanup ----
cts.Cancel();
cts.Dispose();
Application.Shutdown();

// ---- Setup helpers ----

static IConfiguration LoadConfiguration()
{
    return new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables("AETHER_")
        .Build();
}

static IServiceProvider BuildServices(IConfiguration configuration)
{
    var services = new ServiceCollection();

    services.AddSingleton<IConfiguration>(configuration);

    services.AddSingleton(provider =>
    {
        var config = provider.GetRequiredService<IConfiguration>();
        var databasePath = config["database:path"] ?? "store/aether.db";
        var schemaPath = ResolvePath(config["database:schema"] ?? Path.Combine("Data", "Schema.sql"));
        return new AetherDb(databasePath, schemaPath);
    });

    services.AddSingleton<SessionManager, SessionManager>();
    services.AddSingleton<FileMemory>(provider =>
    {
        var config = provider.GetRequiredService<IConfiguration>();
        var groupsPath = config["groups:path"] ?? "groups";
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
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
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
        var agentName = configuration["agent:name"] ?? "maria";
        var agentsRoot = configuration["agent:root"] ?? ".";
        return new AgentProfile(agentName, agentsRoot, config, new AgentModelConfig());
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
    
    // Database Memory System
    services.AddSingleton<SqliteMemorySystem>(provider =>
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var profile = provider.GetRequiredService<AgentProfile>();
        var databasePath = configuration["database:path"] ?? "store/aether.db";
        var memoryFilePath = Path.Combine(profile.AgentDirectory, "MEMORY.md");
        var logger = provider.GetRequiredService<ILogger<SqliteMemorySystem>>();
        return new SqliteMemorySystem(databasePath, memoryFilePath, logger, null);
    });

    // ---- CHANNEL INTEGRATION SERVICES ----
    services.AddSingleton<ConfigLoader>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<ConfigLoader>>();
        return new ConfigLoader(configuration, ".", logger, null);
    });
    
    services.AddSingleton<ChannelAccess>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<ChannelAccess>>();
        return new ChannelAccess("tui", ".", logger);
    });
    
    services.AddSingleton<ChannelMessageQueue>();
    services.AddSingleton<MessageRouter>(provider =>
    {
        var configLoader = provider.GetRequiredService<ConfigLoader>();
        var logger = provider.GetRequiredService<ILogger<MessageRouter>>();
        var db = provider.GetRequiredService<AetherDb>();
        return new MessageRouter(db, provider.GetRequiredService<ChannelMessageQueue>());
    });
    services.AddSingleton<SlashCommandHandler>();
    
    services.AddSingleton<ProviderRoutingOptions>(new ProviderRoutingOptions { });
    
    services.AddSingleton<IReadOnlyList<ILLMProvider>>(provider => 
        provider.GetServices<ILLMProvider>().ToList());

    services.AddSingleton<ProviderRouter>();

    // The TUI Channel itself
    services.AddSingleton<IChannel>(provider =>
    {
        return new Aether.Tui.TuiChannel(
            text => {}, 
            chunk => {}, 
            () => {}
        );
    });

    // The core processor that connects IChannel to AetherSoul
    services.AddHostedService<ChannelMessageProcessor>();

    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

    return services.BuildServiceProvider();
}

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path)) return path;
    var cwdPath = Path.GetFullPath(path);
    if (File.Exists(cwdPath)) return cwdPath;
    return Path.Combine(AppContext.BaseDirectory, path);
}
