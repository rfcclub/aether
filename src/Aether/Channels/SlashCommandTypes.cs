using Aether.Ui;

namespace Aether.Channels;

public sealed record SlashCommandContext(
    string Text,
    string AgentName,
    string WorkspacePath,
    IServiceProvider Services);

public sealed record SlashCommandResult(string Text, bool AutoGreet = false)
{
    public UiDocument? InteractiveUi { get; init; }
}
