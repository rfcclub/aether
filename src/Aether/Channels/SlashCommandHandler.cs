using System.Collections.Concurrent;
using Aether.Agent;
using Aether.Config;
using Aether.Memory;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

public sealed class SlashCommandHandler
{
    /// <summary>
    /// Tracks agents that have a pending session reset prompt to inject on the next user turn.
    /// </summary>
    public static readonly ConcurrentDictionary<string, bool> PendingSessionReset = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _rootServices;
    private readonly ProviderRouter? _providerRouter;
    private readonly ConfigLoader? _configLoader;
    private readonly ILogger<SlashCommandHandler> _logger;

    public SlashCommandHandler(
        IServiceProvider rootServices,
        ILogger<SlashCommandHandler> logger,
        ProviderRouter? providerRouter = null,
        ConfigLoader? configLoader = null)
    {
        _rootServices = rootServices;
        _logger = logger;
        _providerRouter = providerRouter ?? rootServices.GetService<ProviderRouter>();
        _configLoader = configLoader ?? rootServices.GetService<ConfigLoader>();
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
            "/models" => HandleModelsAsync(ctx),
            "/context" => await HandleContextAsync(ctx, ct),
            "/compact" => await HandleCompactAsync(ctx, ct),
            _ => null
        };
    }

    /// <summary>
    /// Returns true if this slash command result was from /new or /reset
    /// and the caller should also trigger an LLM greeting.
    /// </summary>
    public static bool ShouldAutoGreet(SlashCommandResult result) =>
        result.AutoGreet;

    private async Task<SlashCommandResult> HandleNewAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var sessionMgr = _rootServices.GetRequiredService<SessionManager>();
        var session = await sessionMgr.CreateSessionAsync(ctx.WorkspacePath, ct);
        var memory = _rootServices.GetRequiredService<FileMemory>();
        memory.CompactContext(0);

        PendingSessionReset[ctx.AgentName] = true;
        _rootServices.GetService<AetherSoul>()?.Reset();

        _logger.LogInformation("New session {SessionId} created for agent {Agent}", session.Id, ctx.AgentName);
        return new SlashCommandResult($"New session: {session.Id}", AutoGreet: true);
    }

    private Task<SlashCommandResult> HandleResetAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var memory = _rootServices.GetRequiredService<FileMemory>();
        memory.CompactContext(0);

        PendingSessionReset[ctx.AgentName] = true;
        _rootServices.GetService<AetherSoul>()?.Reset();

        _logger.LogInformation("Context cleared for {Agent}", ctx.AgentName);
        return Task.FromResult(new SlashCommandResult("Context cleared.", AutoGreet: true));
    }

    private async Task<SlashCommandResult> HandleModelAsync(SlashCommandContext ctx, string args, CancellationToken ct)
    {
        // No args: show current model + available models
        if (string.IsNullOrWhiteSpace(args))
        {
            var current = _providerRouter?.EffectiveModel ?? "none";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Current model: {current}");
            sb.AppendLine();

            var available = _providerRouter?.GetAvailableModels();
            if (available is { Count: > 0 })
            {
                sb.AppendLine("Available models:");
                foreach (var (provider, model) in available)
                {
                    var marker = string.Equals(model, current, StringComparison.OrdinalIgnoreCase) ? " ← current" : "";
                    sb.AppendLine($"  [{provider}] {model}{marker}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Usage: /model <provider>/<model>");
            return new SlashCommandResult(sb.ToString().TrimEnd());
        }

        // Switch model
        var newPrimary = args.Trim();

        // Verify model resolves to a provider
        var resolvedProvider = _providerRouter?.ResolveModelToProvider(newPrimary);
        if (resolvedProvider is null)
        {
            var available = _providerRouter?.GetAvailableModels() ?? Array.Empty<(string, string)>();
            var models = string.Join(", ", available.Select(m => m.Model));
            return new SlashCommandResult($"Unknown model: {newPrimary}\nAvailable: {models}");
        }

        // Update in-memory chain
        if (_providerRouter is not null)
        {
            var existing = _providerRouter.ModelChain?.Skip(1).ToList() ?? new List<string>();
            var newChain = new List<string> { newPrimary };
            newChain.AddRange(existing.Where(f => f != newPrimary));
            _providerRouter.ModelChain = newChain;
        }

        // Persist to config.json so it survives restart
        if (_configLoader is not null)
        {
            await _configLoader.UpdateAgentModelAsync(ctx.AgentName, newPrimary, ct);
        }

        _logger.LogInformation("Model switched to {Model} for {Agent}", newPrimary, ctx.AgentName);
        return new SlashCommandResult($"Model changed to: {newPrimary} [{resolvedProvider.Name}]\nSurvives restart.");
    }

    private SlashCommandResult HandleModelsAsync(SlashCommandContext ctx)
    {
        var available = _providerRouter?.GetAvailableModels();
        if (available is not { Count: > 0 })
            return new SlashCommandResult("No models available.");

        var current = _providerRouter?.EffectiveModel ?? "";
        var sb = new System.Text.StringBuilder();

        // Group by provider name
        var grouped = available
            .GroupBy(m => m.Provider, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            sb.AppendLine($"[{group.Key}]");
            foreach (var (_, model) in group)
            {
                var marker = string.Equals(model, current, StringComparison.OrdinalIgnoreCase) ? " *" : "";
                sb.AppendLine($"  {model}{marker}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("* = current | /model <provider>/<model> to switch");

        return new SlashCommandResult(sb.ToString().TrimEnd());
    }

    private async Task<SlashCommandResult> HandleContextAsync(SlashCommandContext ctx, CancellationToken ct)
    {
        var memory = _rootServices.GetRequiredService<FileMemory>();
        var sessionMgr = _rootServices.GetRequiredService<SessionManager>();
        var contextEntries = memory.GetContext();
        var tokenEstimate = contextEntries.Sum(e => EstimateTokens(e.Content));

        var model = _providerRouter?.EffectiveModel ?? "none";

        var session = await sessionMgr.GetOrCreateSessionAsync(ctx.WorkspacePath, ct);
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
        var memory = _rootServices.GetRequiredService<FileMemory>();
        memory.CompactContext(4000);

        var remaining = memory.GetContext();
        var tokenEstimate = remaining.Sum(e => EstimateTokens(e.Content));

        _logger.LogInformation("Context compacted for {Agent}, ~{Tokens} tokens remaining", ctx.AgentName, tokenEstimate);
        return Task.FromResult(new SlashCommandResult($"Context compacted to ~{tokenEstimate} tokens"));
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, text.Length / 4);
    }
}
