using Aether.Memory;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

public sealed class SlashCommandHandler : ISlashCommandHandler
{
    private readonly IServiceProvider _rootServices;
    private readonly ProviderRouter? _providerRouter;
    private readonly ILogger<SlashCommandHandler> _logger;

    public SlashCommandHandler(IServiceProvider rootServices, ILogger<SlashCommandHandler> logger, ProviderRouter? providerRouter = null)
    {
        _rootServices = rootServices;
        _logger = logger;
        _providerRouter = providerRouter ?? rootServices.GetService<ProviderRouter>();
    }

    public async Task<SlashCommandResult?> HandleAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var text = ctx.Text.Trim();
        if (text.Length == 0 || text[0] != '/')
            return null;

        var spaceIdx = text.IndexOf(' ');
        var command = spaceIdx < 0 ? text : text[..spaceIdx];
        var args = spaceIdx < 0 ? "" : text[(spaceIdx + 1)..].Trim();

        return command.ToLowerInvariant() switch
        {
            "/new" => await HandleNewAsync(ctx, ct),
            "/reset" => await HandleResetAsync(ctx, ct),
            "/model" => await HandleModelAsync(ctx, args, ct),
            "/context" => await HandleContextAsync(ctx, ct),
            "/compact" => await HandleCompactAsync(ctx, ct),
            _ => null
        };
    }

    private async Task<SlashCommandResult> HandleNewAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var sessionMgr = _rootServices.GetRequiredService<ISessionManager>();
        var session = await sessionMgr.GetOrCreateSessionAsync(ctx.AgentName, ct);
        var memory = _rootServices.GetRequiredService<IMemorySystem>();
        memory.CompactContext(0); // clear all ephemeral context

        _logger.LogInformation("New session {SessionId} created for agent {Agent}", session.Id, ctx.AgentName);
        return new SlashCommandResult($"New session: {session.Id}");
    }

    private Task<SlashCommandResult> HandleResetAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var memory = _rootServices.GetRequiredService<IMemorySystem>();
        memory.CompactContext(0); // clear all ephemeral context

        _logger.LogInformation("Context cleared for {Agent}", ctx.AgentName);
        return Task.FromResult(new SlashCommandResult("Context cleared."));
    }

    private Task<SlashCommandResult> HandleModelAsync(SlashCommandContext ctx, string args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            var current = _providerRouter?.EffectiveModel ?? "none";
            var fallbacks = _providerRouter?.ModelChain is { Count: > 1 } chain
                ? $" (fallbacks: {string.Join(", ", chain.Skip(1))})"
                : "";
            return Task.FromResult(new SlashCommandResult($"Model: {current}{fallbacks}"));
        }

        // Switch model
        var newPrimary = args;
        if (_providerRouter is not null)
        {
            var existing = _providerRouter.ModelChain?.Skip(1).ToList() ?? new List<string>();
            var newChain = new List<string> { newPrimary };
            newChain.AddRange(existing);
            _providerRouter.ModelChain = newChain;
        }

        _logger.LogInformation("Model switched to {Model} for {Agent}", newPrimary, ctx.AgentName);
        return Task.FromResult(new SlashCommandResult($"Model changed to: {newPrimary}"));
    }

    private async Task<SlashCommandResult> HandleContextAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var memory = _rootServices.GetRequiredService<IMemorySystem>();
        var sessionMgr = _rootServices.GetRequiredService<ISessionManager>();
        var contextEntries = memory.GetContext();
        var tokenEstimate = contextEntries.Sum(e => EstimateTokens(e.Content));

        var model = _providerRouter?.EffectiveModel ?? "none";

        var session = await sessionMgr.GetOrCreateSessionAsync(ctx.AgentName, ct);
        var history = await sessionMgr.GetHistoryAsync(session.Id, 500, ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Session: {ctx.AgentName} ({session.Id})");
        sb.AppendLine($"Model: {model}");
        sb.AppendLine($"Messages in session: {history.Count}");
        sb.AppendLine($"Ephemeral entries: {contextEntries.Count} (~{tokenEstimate} tokens)");

        return new SlashCommandResult(sb.ToString().TrimEnd());
    }

    private Task<SlashCommandResult> HandleCompactAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var memory = _rootServices.GetRequiredService<IMemorySystem>();
        memory.CompactContext(4000);

        var remaining = memory.GetContext();
        var tokenEstimate = remaining.Sum(e => EstimateTokens(e.Content));

        _logger.LogInformation("Context compacted for {Agent}, ~{Tokens} tokens remaining", ctx.AgentName, tokenEstimate);
        return Task.FromResult(new SlashCommandResult($"Context compacted to ~{tokenEstimate} tokens"));
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // Rough estimate: ~4 chars per token
        return Math.Max(1, text.Length / 4);
    }
}
