namespace Aether.Channels;

public sealed record SlashCommandContext(
    string Text,
    string AgentName,
    string WorkspacePath,
    IServiceProvider Services);

public sealed record SlashCommandResult(string Text);
