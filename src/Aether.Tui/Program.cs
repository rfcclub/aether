using Aether;
using Aether.Agent;
using Aether.Agents;
using Aether.Data;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Gui;
using ToolExecutor = Aether.Agent.ToolExecutor;

var configuration = LoadConfiguration();
var services = BuildServices(configuration);
var provider = services.GetRequiredService<IServiceProvider>();

Application.Init();

var top = Application.Top;
top.ColorScheme = Colors.Base;

var groups = LoadGroups(configuration);
var selectedGroup = groups.FirstOrDefault() ?? "main";

// ---- Scrollback state ----
var userScrolledUp = false;
var newMessagesAvailable = false;

// ---- Build UI ----

var win = new Window("Aether")
{
    X = 0, Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill()
};

var leftPane = new FrameView("Groups")
{
    X = 0, Y = 0,
    Width = Dim.Percent(30),
    Height = Dim.Fill(3)
};

var groupList = new ListView(groups)
{
    X = 0, Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill()
};
groupList.SelectedItem = groups.IndexOf(selectedGroup);
leftPane.Add(groupList);

var rightPane = new FrameView("Chat")
{
    X = Pos.Right(leftPane),
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(3)   // leave room for input + status
};

var chatView = new TextView
{
    X = 0,
    Y = 0,
    Width = Dim.Fill(),
    Height = Dim.Fill(),
    ReadOnly = true,
    WordWrap = true,
    CanFocus = true
};
rightPane.Add(chatView);

var inputFrame = new FrameView(null)
{
    X = 0,
    Y = Pos.Bottom(rightPane),
    Width = Dim.Fill(),
    Height = 3
};

var inputField = new TextField("")
{
    X = 1,
    Y = 0,
    Width = Dim.Fill(10),
    Height = 1
};

var sendButton = new Button("Send")
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

win.Add(leftPane);
win.Add(rightPane);
win.Add(inputFrame);

// ---- StatusBar ----
// Use Key.Null for display-only items so they don't register global hotkeys.
// Only the Quit item gets a real shortcut binding.
var sbGroupItem = new StatusItem(Key.Null, "Group: main", null);
var sbWrapItem = new StatusItem(Key.Null, "Wrap: ON", null);
var sbScrollItem = new StatusItem(Key.Null, "", null);
var sbQuitItem = new StatusItem(Key.Q | Key.CtrlMask, "~Ctrl+Q~ Quit", () => Application.RequestStop());
var sbSwitchItem = new StatusItem(Key.Null, "~F5~ Switch", null);
var sbWrapToggleItem = new StatusItem(Key.Null, "~Ctrl+W~ Wrap", null);

var statusBar = new StatusBar(
    new[] { sbGroupItem, sbWrapItem, sbScrollItem, sbQuitItem, sbSwitchItem, sbWrapToggleItem });

top.Add(win);
top.Add(statusBar);

// ---- Helpers ----

var cts = new CancellationTokenSource();
var soul = provider.GetRequiredService<AetherSoul>();

/// Returns the total number of lines in the chat buffer.
static int GetTotalLines(TextView view)
{
    // v1.19: Lines is an int, the number of text rows in the buffer
    return view.Lines;
}

/// Approximate number of visible lines in the view area (excluding borders).
static int GetVisibleLines(TextView view)
{
    // Frame.Height includes borders; subtract 2 for top/bottom border
    var frameHeight = view.Frame.Height;
    return Math.Max(1, frameHeight - 2);
}

/// Updates the scroll position indicator in the status bar.
/// Shows "L{top+1}-{bottom}/{total}" or "↓ New" when there are unread messages
/// below the current viewport.
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

/// Updates all status bar labels (group, wrap, scroll position) in one call.
void UpdateStatusBar()
{
    sbGroupItem.Title = $"Group: {selectedGroup}";
    sbWrapItem.Title = chatView.WordWrap ? "Wrap: ON" : "Wrap: OFF";
    UpdateScrollIndicator();
    Application.Refresh();
}

/// Appends text to the chat view. Auto-scrolls to the bottom unless the user
/// has scrolled up manually. If the user is scrolled up and new content arrives,
/// displays a "↓ New" indicator.
void AppendChat(string text)
{
    chatView.Text += text + Environment.NewLine;

    if (!userScrolledUp)
    {
        // Auto-scroll to bottom
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

/// Scrolls the chat view up by the given number of lines.
void ScrollUp(int lines)
{
    var target = Math.Max(0, chatView.TopRow - lines);
    chatView.ScrollTo(target, true);
    userScrolledUp = chatView.TopRow < GetTotalLines(chatView) - 1;
    UpdateStatusBar();
}

/// Scrolls the chat view down by the given number of lines.
/// If the bottom is reached, auto-scroll resumes.
void ScrollDown(int lines)
{
    var total = GetTotalLines(chatView);
    var visible = GetVisibleLines(chatView);
    var maxTop = Math.Max(0, total - visible);
    var target = Math.Min(maxTop, chatView.TopRow + lines);
    chatView.ScrollTo(target, true);

    // If user scrolled to end, resume auto-scroll
    if (chatView.TopRow >= maxTop)
    {
        userScrolledUp = false;
        newMessagesAvailable = false;
    }
    UpdateStatusBar();
}

/// Jumps to the bottom of the chat view and resumes auto-scroll.
void ScrollToBottom()
{
    var total = GetTotalLines(chatView);
    chatView.ScrollTo(total - 1, true);
    userScrolledUp = false;
    newMessagesAvailable = false;
    UpdateStatusBar();
}

/// Sends a user message to AetherSoul and displays the response.
async void SendMessage()
{
    var message = inputField.Text?.ToString()?.Trim();
    if (string.IsNullOrWhiteSpace(message)) return;

    inputField.Text = "";
    AppendChat($"You: {message}");
    statusLabel.Text = "Thinking...";
    Application.Refresh();

    try
    {
        var response = await Task.Run(
            () => soul.ProcessAsync(selectedGroup, message, cts.Token),
            cts.Token);

        Application.MainLoop.Invoke(() =>
        {
            var lines = response.Content.Split(Environment.NewLine);
            foreach (var line in lines)
            {
                AppendChat(line);
            }
            statusLabel.Text = "Ready";
            UpdateStatusBar();
            Application.Refresh();
        });
    }
    catch (Exception ex)
    {
        Application.MainLoop.Invoke(() =>
        {
            AppendChat($"[Error: {ex.Message}]");
            statusLabel.Text = "Error";
            Application.Refresh();
        });
    }
}

/// Switches to the given group, clearing the chat view and resetting scroll state.
void SwitchGroup(string group)
{
    selectedGroup = group;
    rightPane.Title = $"Chat — {group}";
    chatView.Text = $"Switched to group: {group}" + Environment.NewLine;
    userScrolledUp = false;
    newMessagesAvailable = false;
    statusLabel.Text = $"Group: {group}";
    UpdateStatusBar();
    Application.Refresh();
}

/// Cycles through available groups in order. Wraps around at the end.
void CycleGroup()
{
    if (groups.Count == 0) return;

    var currentIndex = groups.IndexOf(selectedGroup);
    var nextIndex = (currentIndex + 1) % groups.Count;
    SwitchGroup(groups[nextIndex]);
    groupList.SelectedItem = nextIndex;
}

/// Toggles word wrap on the chat view.
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

groupList.SelectedItemChanged += args =>
{
    if (args.Item >= 0 && args.Item < groups.Count)
    {
        SwitchGroup(groups[args.Item]);
    }
};

// ---- Global key handling ----
// RootKeyEvent fires before any view gets the key event.
// Returns true to suppress further processing.
Application.RootKeyEvent += keyEvent =>
{
    switch (keyEvent.Key)
    {
        case Key.F5:
            CycleGroup();
            return true;

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

    // Let other keys propagate normally
    return false;
};

// ---- Mouse wheel scrolling ----
// Forward mouse wheel events to scroll the chat view.
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

// ---- Initial state ----
rightPane.Title = $"Chat — {selectedGroup}";
chatView.Text = $"Aether TUI ready. {groups.Count} group(s) loaded.{Environment.NewLine}";
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
    services.AddSingleton<ToolRegistry, ToolRegistry>();
    services.AddSingleton<ToolExecutor, ToolExecutor>();
    services.AddSingleton<SkillRegistry, SkillRegistry>();
    services.AddSingleton<SkillParser, SkillParser>();
    services.AddSingleton<SkillTrigger, SkillTrigger>();
    services.AddSingleton<SkillEvolution, SkillEvolution>();

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
        var agentName = configuration["agent:name"] ?? "aether";
        var agentsRoot = configuration["agent:root"] ?? ".";
        return new AgentProfile(agentName, agentsRoot, config);
    });

    services.AddSingleton<AetherSoul>(provider =>
    {
        var llm = provider.GetRequiredService<ILLMProvider>();
        var memory = provider.GetRequiredService<FileMemory>();
        var tools = provider.GetRequiredService<ToolExecutor>();
        var sessions = provider.GetRequiredService<SessionManager>();
        var skills = provider.GetRequiredService<SkillRegistry>();
        var skillTrigger = provider.GetRequiredService<SkillTrigger>();
        var profile = provider.GetRequiredService<AgentProfile>();
        return new AetherSoul(llm, memory, tools, sessions, skills, skillTrigger, profile);
    });

    services.AddSingleton<ILoggerFactory>(_ => NullLoggerFactory.Instance);

    return services.BuildServiceProvider();
}

static List<string> LoadGroups(IConfiguration configuration)
{
    var groupsPath = configuration["groups:path"] ?? "groups";
    if (!Directory.Exists(groupsPath))
        return new List<string> { "main" };

    return Directory.GetDirectories(groupsPath)
        .Select(Path.GetFileName)
        .Where(name => name is not null)
        .Cast<string>()
        .OrderBy(n => n)
        .ToList();
}

static string ResolvePath(string path)
{
    if (Path.IsPathRooted(path)) return path;
    var cwdPath = Path.GetFullPath(path);
    if (File.Exists(cwdPath)) return cwdPath;
    return Path.Combine(AppContext.BaseDirectory, path);
}
