# Design: Aether Terminal GUI (Avalonia)

## Architecture

```
┌──────────────────────────────────────────────┐
│              Aether.Terminal                  │
│  ┌────────────────────────────────────────┐  │
│  │        MainWindow (Avalonia)           │  │
│  │  ┌──────────────────────────────────┐  │  │
│  │  │ TitleBar  agent:model session    │  │  │
│  │  ├──────────────────────────────────┤  │  │
│  │  │                                 │  │  │
│  │  │ ChatView (ScrollViewer)         │  │  │
│  │  │ ┌─────────────────────────────┐ │  │  │
│  │  │ │ user> hello                 │ │  │  │
│  │  │ │ aria> Hi there!             │ │  │  │
│  │  │ │ [tool: file_read /tmp/x]   │ │  │  │
│  │  │ │ tool> file contents...      │ │  │  │
│  │  │ │ ▊ thinking...               │ │  │  │
│  │  │ └─────────────────────────────┘ │  │  │
│  │  │                                 │  │  │
│  │  ├──────────────────────────────────┤  │  │
│  │  │ InputLine  > _                  │  │  │
│  │  └──────────────────────────────────┘  │  │
│  │                                        │  │
│  │  ┌───────────┐  ┌──────────────────┐   │  │
│  │  │ StatusBar  │  │ ThemeSelector    │   │  │
│  │  └───────────┘  └──────────────────┘   │  │
│  └────────────────────────────────────────┘  │
│                     │                         │
│                     ▼                         │
│  ┌────────────────────────────────────────┐  │
│  │     TerminalViewModel (MVVM)           │  │
│  │  ObservableCollection<ChatMessage>     │  │
│  │  ICommand SendCommand                  │  │
│  │  bridges UI ↔ AgentLoop                │  │
│  └────────────────────────────────────────┘  │
│                     │                         │
└─────────────────────┼─────────────────────────┘
                      │
                      ▼
┌──────────────────────────────────────────────┐
│              Aether (core)                    │
│  AgentLoop  ←→  ProviderRegistry  ←→  Tools  │
└──────────────────────────────────────────────┘
```

## Component Design

### MainWindow
Avalonia Window with dark background, monospace font. Three zones: TitleBar, ChatView, InputLine. Loads theme from styles.

### TerminalViewModel (MVVM)

```csharp
public class TerminalViewModel : ViewModelBase
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public string InputText { get; set; } = "";
    public string StatusText { get; set; } = "Ready";
    public string AgentName { get; set; } = "";
    public bool IsThinking { get; set; } = false;
    
    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ThemeCommand { get; }
    
    // Called when user submits input
    async Task SendMessage(string content);
    
    // Listens on AgentLoop outbound channel
    async Task ListenForResponses(CancellationToken ct);
}
```

### ChatMessage Model
```csharp
public record ChatMessage(
    string Id,
    ChatRole Role,       // User, Assistant, Tool, System
    string Content,
    string? ToolName = null,
    string? ToolResult = null,
    DateTime Timestamp = default
);
```

### ChatView
Avalonia ListBox/ItemsControl with DataTemplate per role:
- User: right-aligned, muted foreground
- Assistant: left-aligned, primary foreground
- Tool calls: amber foreground, collapsible via Expander
- System: dim, small font, centered

### InputLine
TextBox at bottom. Enter submits. Up-arrow history. Supports:
- `/theme matrix|amber|mono` — switch theme
- `/clear` — clear chat
- `/status` — toggle status bar detail
- `/exit` or Ctrl+D — quit

### TitleBar
Top strip: agent name, active model, session ID, green dot (connected) or red dot (disconnected).

### Theme System
Avalonia styles in XAML resource dictionaries:
- `MatrixTheme.axaml`: #00ff00 on #0a0a0a
- `AmberTheme.axaml`: #ffb000 on #1a1a0a
- `MonoTheme.axaml`: #e0e0e0 on #1a1a1a

Theme switching: `Application.Current.Resources.MergedDictionaries` swap.

## Data Flow

```
User types "hello" → Enter
  → SendCommand.Execute()
  → TerminalViewModel.SendMessage("hello")
  → Messages.Add(new ChatMessage(Role: User, "hello"))
  → IsThinking = true (shows indicator)
  → AgentLoop.Inbound.Post(message)
  
AgentLoop processes...
  → AgentLoop.Outbound receives response
  → TerminalViewModel.ListenForResponses picks it up
  → AvaloniaSynchronizationContext.Post(() => {
        Messages.Add(new ChatMessage(Role: Assistant, response.Content))
        IsThinking = false
    })
```

## Threading

- Avalonia UI thread: all UI updates (via `AvaloniaSynchronizationContext`)
- AgentLoop: uses TPL Dataflow / Channels (existing)
- TerminalViewModel: bridges via `AvaloniaSynchronizationContext.Post` for UI, `Task.Run` for outbound listener

## Project Structure

```
src/Aether.Terminal/
├── Aether.Terminal.csproj
├── MainWindow.axaml              # Window layout
├── MainWindow.axaml.cs           # Code-behind
├── TerminalViewModel.cs          # MVVM ViewModel
├── Models/
│   └── ChatMessage.cs            # Message model
├── Views/
│   ├── ChatView.axaml            # Chat display
│   └── ChatBubble.axaml          # Individual message template
├── Themes/
│   ├── MatrixTheme.axaml
│   ├── AmberTheme.axaml
│   └── MonoTheme.axaml
└── App.axaml                     # Application entry, theme loading
```

## Dependencies

- `Avalonia.Desktop` — UI framework
- `Avalonia.Themes.Fluent` — base theme (we override)
- `CommunityToolkit.Mvvm` — MVVM source generators (optional, for cleaner code)
- `Aether` (project reference) — AgentLoop, core types

## What's NOT in Scope

- Streaming token display (requires AgentLoop streaming support)
- Multi-tab conversations
- Remote ACP connection (local GUI only)
- Plugin/mcp extension UI
