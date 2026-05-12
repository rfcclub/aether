using System.Collections.Concurrent;
using Aether.Agent;
using Aether.Config;
using Aether.Memory;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Aether.Tooling;
using Aether.Ui;
using Aether.Ui.Handlers;
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
    private readonly ModelSelectionHandler? _modelHandler;
    private readonly ILogger<SlashCommandHandler> _logger;

    public SlashCommandHandler(
        IServiceProvider rootServices,
        ILogger<SlashCommandHandler> logger,
        ProviderRouter? providerRouter = null,
        ConfigLoader? configLoader = null,
        ModelSelectionHandler? modelHandler = null)
    {
        _rootServices = rootServices;
        _logger = logger;
        _providerRouter = providerRouter ?? rootServices.GetService<ProviderRouter>();
        _configLoader = configLoader ?? rootServices.GetService<ConfigLoader>();
        _modelHandler = modelHandler ?? rootServices.GetService<ModelSelectionHandler>();
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
            "/models" => await HandleModelsAsync(ctx),
            "/tools" => HandleTools(),
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
        // If interactive handler is available, delegate to it
        if (_modelHandler is not null && _providerRouter is not null)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                // /model — show provider list
                var doc = await _modelHandler.HandleAsync(
                    new UiCallback { Namespace = "model", Action = "browse" },
                    _rootServices, ctx.AgentName);
                if (doc is not null)
                {
                    return new SlashCommandResult("Models:") { InteractiveUi = doc };
                }
            }
            else
            {
                // /model <provider>/<model> — direct switch
                var modelId = args.Trim();
                var doc = await _modelHandler.HandleAsync(
                    new UiCallback { Namespace = "model", Action = "select", Data = modelId },
                    _rootServices, ctx.AgentName);
                if (doc is not null)
                {
                    return new SlashCommandResult($"Model changed to: {modelId}")
                        { InteractiveUi = doc };
                }
            }
        }

        // Fallback: text-only /model (no interactive handler available)
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

        // Switch model (text-only fallback)
        var newPrimary = args.Trim();

        var resolvedProvider = _providerRouter?.ResolveModelToProvider(newPrimary);
        if (resolvedProvider is null)
        {
            var available = _providerRouter?.GetAvailableModels() ?? Array.Empty<(string, string)>();
            var models = string.Join(", ", available.Select(m => m.Model));
            return new SlashCommandResult($"Unknown model: {newPrimary}\nAvailable: {models}");
        }

        if (_providerRouter is not null)
        {
            var existing = _providerRouter.ModelChain?.Skip(1).ToList() ?? new List<string>();
            var newChain = new List<string> { newPrimary };
            newChain.AddRange(existing.Where(f => f != newPrimary));
            _providerRouter.ModelChain = newChain;
        }

        if (_configLoader is not null)
        {
            await _configLoader.UpdateAgentModelAsync(ctx.AgentName, newPrimary, ct);
        }

        _logger.LogInformation("Model switched to {Model} for {Agent}", newPrimary, ctx.AgentName);
        return new SlashCommandResult($"Model changed to: {newPrimary} [{resolvedProvider.Name}]\nSurvives restart.");
    }

    private async Task<SlashCommandResult> HandleModelsAsync(SlashCommandContext ctx)
    {
        // If interactive handler is available, use it
        if (_modelHandler is not null && _providerRouter is not null)
        {
            var doc = await _modelHandler.HandleAsync(
                new UiCallback { Namespace = "model", Action = "browse" },
                _rootServices, ctx.AgentName);
            if (doc is not null)
            {
                return new SlashCommandResult("Models:") { InteractiveUi = doc };
            }
        }

        // Text-only fallback
        var available = _providerRouter?.GetAvailableModels();
        if (available is not { Count: > 0 })
            return new SlashCommandResult("No models available.");

        var current = _providerRouter?.EffectiveModel ?? "";
        var sb = new System.Text.StringBuilder();

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

    private SlashCommandResult HandleTools()
    {
        var registry = _rootServices.GetService<ToolRegistry>();
        if (registry is null)
            return new SlashCommandResult("Tool registry is not available.");

        var audit = registry.Audit();
        var visible = audit.Where(t => t.Enabled).ToList();
        var disabled = audit.Where(t => !t.Enabled).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Visible tools: {visible.Count}");
        if (visible.Count > 0)
            sb.AppendLine($"Enabled: {string.Join(", ", visible.Select(t => t.Name))}");
        if (disabled.Count > 0)
        {
            sb.AppendLine("Disabled:");
            foreach (var tool in disabled)
                sb.AppendLine($"  {tool.Name}: {tool.DisabledReason ?? "disabled"}");
        }

        var visibleNames = visible.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = OpenClawParityCandidates
            .Where(name => !visibleNames.Contains(name))
            .ToList();
        if (missing.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Missing OpenClaw parity candidates: {string.Join(", ", missing)}");
        }

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

    private static readonly string[] OpenClawParityCandidates =
    {
        "message",
        "sessions_spawn",
        "sessions_send",
        "sessions_history",
        "subagents",
        "cron",
        "nodes",
        "image_generate",
        "video_generate",
        "music_generate",
        "pdf",
        "tts"
    };
}
