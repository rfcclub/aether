using System.Collections.ObjectModel;
using System.Windows.Input;
using Aether.Agent;
using Aether.Agents;
using Aether.Data;
using Aether.Terminal.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Aether.Terminal;

public partial class TerminalViewModel : ObservableObject
{
    private readonly AetherSoul _soul;
    private readonly GoalStore _goalStore;
    private readonly AgentProfile _profile;
    private readonly string _workspacePath;
    private readonly ILogger<TerminalViewModel> _logger;
    private readonly List<string> _inputHistory = new();
    private readonly DispatcherTimer _refreshTimer;
    private int _historyIndex = -1;

    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<Goal> ActiveGoals { get; } = new();

    [ObservableProperty]
    private string _inputText = "";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _agentName = "";

    [ObservableProperty]
    private string _modelName = "";

    [ObservableProperty]
    private string _continuityText = "";

    [ObservableProperty]
    private int _tensionLevel = 20;

    [ObservableProperty]
    private bool _isHiveActive = true;

    [ObservableProperty]
    private bool _isThinking;

    [ObservableProperty]
    private double _systemHeat = 30.0;

    [ObservableProperty]
    private string _currentTheme = "Mono";

    public TerminalViewModel(
        AetherSoul soul,
        GoalStore goalStore,
        AgentProfile profile,
        string workspacePath,
        string agentName,
        string modelName,
        ILogger<TerminalViewModel> logger)
    {
        _soul = soul;
        _goalStore = goalStore;
        _profile = profile;
        _workspacePath = workspacePath;
        _agentName = agentName;
        _modelName = modelName;
        _logger = logger;

        SendCommand = new AsyncRelayCommand(SendMessageAsync);
        ClearCommand = new RelayCommand(ClearChat);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshSovereigntyData();
        _refreshTimer.Start();

        // Initial refresh
        _ = RefreshSovereigntyData();
    }

    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }

    public async Task RefreshSovereigntyData()
    {
        try
        {
            // Load Goals
            var goals = await _goalStore.GetActiveGoalsAsync("maria");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ActiveGoals.Clear();
                foreach (var g in goals) ActiveGoals.Add(g);
            });

            // Load Continuity
            var continuity = _profile.LoadFile("CONTINUITY.md") ?? "No continuity data.";
            ContinuityText = continuity;

            // Calculate SystemHeat
            double targetHeat = IsThinking ? 85.0 : (ActiveGoals.Count * 10.0 + 20.0);
            SystemHeat = Math.Clamp(SystemHeat * 0.8 + targetHeat * 0.2, 20.0, 100.0);

            // Mock Lore Indicators update
            TensionLevel = (TensionLevel + 5) % 101;
            IsHiveActive = !IsHiveActive;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh sovereignty data");
        }
    }

    private async Task SendMessageAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Handle slash commands
        if (text.StartsWith('/'))
        {
            HandleSlashCommand(text);
            InputText = "";
            return;
        }

        _inputHistory.Add(text);
        _historyIndex = _inputHistory.Count;
        InputText = "";

        var msgId = Guid.NewGuid().ToString();
        Messages.Add(new ChatMessage(msgId, ChatRole.User, text, Timestamp: DateTime.Now));
        IsThinking = true;
        StatusText = "Thinking...";

        try
        {
            var response = await Task.Run(() => _soul.ProcessAsync("main", text, CancellationToken.None));

            Messages.Add(new ChatMessage(
                Guid.NewGuid().ToString(),
                ChatRole.Assistant,
                response.Content,
                Timestamp: DateTime.Now));

            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent processing failed");
            Messages.Add(new ChatMessage(
                Guid.NewGuid().ToString(),
                ChatRole.System,
                $"Error: {ex.Message}",
                Timestamp: DateTime.Now));
            StatusText = "Error";
        }
        finally
        {
            IsThinking = false;
        }
    }

    private void HandleSlashCommand(string text)
    {
        var parts = text.Split(' ', 2);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";

        switch (cmd)
        {
            case "/theme":
                SetTheme(arg);
                Messages.Add(new ChatMessage(
                    Guid.NewGuid().ToString(),
                    ChatRole.System,
                    $"Theme set to: {CurrentTheme}",
                    Timestamp: DateTime.Now));
                break;
            case "/clear":
                ClearChat();
                break;
            case "/exit":
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                break;
            case "/status":
                Messages.Add(new ChatMessage(
                    Guid.NewGuid().ToString(),
                    ChatRole.System,
                    $"Agent: {AgentName} | Model: {ModelName} | Theme: {CurrentTheme}",
                    Timestamp: DateTime.Now));
                break;
            default:
                Messages.Add(new ChatMessage(
                    Guid.NewGuid().ToString(),
                    ChatRole.System,
                    $"Unknown command: {cmd}. Try /theme, /clear, /status, /exit",
                    Timestamp: DateTime.Now));
                break;
        }
    }

    public void NavigateHistory(bool up)
    {
        if (_inputHistory.Count == 0) return;

        if (up)
        {
            if (_historyIndex > 0)
                _historyIndex--;
        }
        else
        {
            if (_historyIndex < _inputHistory.Count - 1)
                _historyIndex++;
            else
            {
                InputText = "";
                _historyIndex = _inputHistory.Count;
                return;
            }
        }

        InputText = _inputHistory[_historyIndex];
    }

    private void ClearChat()
    {
        Messages.Clear();
        StatusText = "Cleared";
    }

    public void SetTheme(string theme)
    {
        var normalized = theme.ToLowerInvariant() switch
        {
            "matrix" => "Matrix",
            "amber" => "Amber",
            "mono" => "Mono",
            _ => ""
        };

        if (normalized is "") return;
        CurrentTheme = normalized;

        var app = Application.Current;
        if (app is null) return;

        // Clear existing merged dictionaries
        var toRemove = new List<IResourceProvider>();
        foreach (var dict in app.Resources.MergedDictionaries)
        {
            if (dict is ResourceDictionary rd && rd.Count > 0)
                toRemove.Add(dict);
        }
        foreach (var d in toRemove)
            app.Resources.MergedDictionaries.Remove(d);

        // Set theme colors
        var (bg, fg, dim, accent, user, tool, err) = normalized switch
        {
            "Matrix" => ("#0a0a0a", "#00ff00", "#005500", "#00cc00", "#1a3a1a", "#3a2a00", "#ff4444"),
            "Amber" => ("#1a1a0a", "#ffb000", "#554400", "#ffcc00", "#2a2a10", "#2a1a00", "#ff6644"),
            _ => ("#1a1a1a", "#e0e0e0", "#444444", "#ffffff", "#2a2a2a", "#2a2a1a", "#ff6666")
        };

        app.Resources["TerminalBackground"] = ColorBrush(bg);
        app.Resources["TerminalForeground"] = ColorBrush(fg);
        app.Resources["TerminalDim"] = ColorBrush(dim);
        app.Resources["TerminalAccent"] = ColorBrush(accent);
        app.Resources["TerminalUserBubble"] = ColorBrush(user);
        app.Resources["TerminalToolBubble"] = ColorBrush(tool);
        app.Resources["TerminalError"] = ColorBrush(err);
    }

    private static SolidColorBrush ColorBrush(string hex)
        => SolidColorBrush.Parse(hex);
}
