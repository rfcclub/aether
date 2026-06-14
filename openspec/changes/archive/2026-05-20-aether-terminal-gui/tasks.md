## 1. Project Scaffold

- [x] 1.1 Create `src/Aether.Terminal/` directory structure (Models/, Views/, Themes/)
- [x] 1.2 Create `Aether.Terminal.csproj` — net10.0, Avalonia.Desktop + Avalonia.Themes.Fluent + CommunityToolkit.Mvvm + Aether project reference
- [x] 1.3 Add `Aether.Terminal` to `Aether.sln` solution

## 2. Theme System

- [x] 2.1 Create `Themes/MatrixTheme.axaml` — green #00ff00 on dark #0a0a0a
- [x] 2.2 Create `Themes/AmberTheme.axaml` — amber #ffb000 on dark #1a1a0a
- [x] 2.3 Create `Themes/MonoTheme.axaml` — light #e0e0e0 on dark #1a1a1a

## 3. Application Shell

- [x] 3.1 Create `App.axaml` + `App.axaml.cs` — application entry, default theme loading, DI setup with dynamic providers
- [x] 3.2 Create `MainWindow.axaml` + `MainWindow.axaml.cs` — window layout: TitleBar, ChatView, InputLine, StatusBar

## 4. MVVM ViewModel

- [x] 4.1 Create `Models/ChatMessage.cs` — record: Id, Role (User/Assistant/Tool/System), Content, ToolName?, ToolResult?, Timestamp
- [x] 4.2 Create `TerminalViewModel.cs` — ObservableCollection<ChatMessage>, InputText, StatusText, AgentName, IsThinking, SendCommand, ClearCommand, ThemeCommand
- [x] 4.3 Implement `SendMessage()` — add User message, call AetherSoul.ProcessAsync, add Assistant messages
- [x] 4.4 Implement slash commands: `/theme matrix|amber|mono`, `/clear`, `/status`, `/exit`

## 5. Views

- [x] 5.1 Create `Views/ChatView.axaml` — ScrollViewer + ItemsControl with DataTemplate, auto-scroll on new messages
- [x] 5.2 Per-role styling inline in DataTemplate: role label, timestamp, content with theme-aware colors

## 6. Input & Commands

- [x] 6.1 Implement InputLine TextBox — Enter submits, Up-arrow history
- [x] 6.2 Implement slash commands: `/theme matrix|amber|mono`, `/clear`, `/status`, `/exit`

## 7. Program Entry Point

- [x] 7.1 Create `Program.cs` — BuildAvaloniaApp with DI, wire TerminalViewModel, launch window

## 8. Build and Verify

- [x] 8.1 `dotnet build src/Aether.Terminal` — compile with zero errors
- [x] 8.2 `dotnet test` — all existing 314+ tests still pass (3 pre-existing failures unrelated)
