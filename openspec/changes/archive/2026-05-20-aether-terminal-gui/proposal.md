# Aether Terminal GUI — Proposal

## Why

Aether (C# reference runtime, 229 tests) has `Aether.Tui` — a basic console app that reads stdin and prints responses. No scrollback, no history, no visual feedback, no tool call display. It's functional but not usable as a daily agent interface.

Like KuroClaw, Aether needs a beautiful terminal-like GUI. But for C# / .NET ecosystem, the natural choice is Avalonia — a cross-platform XAML-based UI framework similar to WPF. Runs on Linux, macOS, and Windows.

## What Changes

- **New**: `Aether.Terminal` project — Avalonia desktop app styled as terminal emulator. Dark background, monospace font, chat interface, slash commands.

## Approach

Same pattern as KuroClaw Terminal: GUI masquerading as TUI. Avalonia gives us:
- XAML layout (flex-like, strong)
- Data binding (MVVM pattern — clean separation)
- CSS-like styling for terminal themes
- Cross-platform (Linux/Mac/Windows, same as .NET)
- Direct in-process calls to AgentLoop (no serialization needed)

## Capabilities

- `terminal-gui`: Avalonia desktop app with terminal aesthetic for interactive agent interaction
- `theme-system`: XAML styles for terminal themes (matrix green, amber, monochrome)
- `mvvm-architecture`: Clean separation — TerminalViewModel bridges GUI ↔ AgentLoop

## Impact

- New project: `src/Aether.Terminal/`
- New dependency: `Avalonia.Desktop` (NuGet)
- Zero changes to existing Aether projects
- Zero changes to Aether core
