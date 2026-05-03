namespace Aether.Channels;

/// <summary>
/// Pre-LLM interceptor for slash commands (/new, /reset, /model, /context, /compact).
/// Channel-agnostic and framework-agnostic — usable by both Aether and KuroClaw.
/// </summary>
public interface ISlashCommandHandler
{
    /// <summary>
    /// Handle a potential slash command. Returns null if the message is not a recognized
    /// slash command (passthrough to LLM). Returns a result if handled.
    /// </summary>
    Task<SlashCommandResult?> HandleAsync(SlashCommandContext ctx, CancellationToken ct);
}

public sealed record SlashCommandContext(
    string Text,
    string AgentName,
    string WorkspacePath,
    IServiceProvider Services);

public sealed record SlashCommandResult(string Text);
