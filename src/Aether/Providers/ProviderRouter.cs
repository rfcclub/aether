using System.Diagnostics;
using Aether.Config;
using Aether.Data;
using Microsoft.Extensions.Logging;

namespace Aether.Providers;

/// <summary>
/// Intelligent provider routing with fallback chain, circuit breaker,
/// streaming support, and cost tracking.
///
/// Per OpenSpec:
///   - Complexity scoring (0.0-1.0) determines escalation
///   - Circuit breaker opens after 3 failures, resets after 60s
///   - Streaming via IAsyncEnumerable where provider supports
///   - Cost tracking per call to provider_usage table
///   - Per-agent credential resolution: agent auth → global config → env vars
///   - Model fallback chain from agent's models.json
/// </summary>
public class ProviderRouter : ILLMProvider
{
    private readonly IReadOnlyList<ILLMProvider> _providers;
    private readonly ProviderRoutingOptions _options;
    private readonly AetherDb _db;
    private readonly ILogger<ProviderRouter> _logger;
    private readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();
    private readonly object _lock = new();

    private string PrimaryGroup => _options.ProviderPriorities
        .OrderBy(kv => kv.Value)
        .Select(kv => kv.Key)
        .FirstOrDefault() ?? "openrouter";

    private List<string> FallbackGroups => _options.ProviderPriorities
        .OrderBy(kv => kv.Value)
        .Select(kv => kv.Key)
        .Where(g => g != PrimaryGroup)
        .ToList();

    private const int CircuitFailureThreshold = 3;
    private static readonly TimeSpan CircuitResetTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Current agent spec for per-agent credential and model resolution.
    /// Set before calling CompleteAsync to enable per-agent overrides.
    /// </summary>
    public AgentSpecConfig? CurrentAgent { get; set; }

    /// <summary>
    /// Model fallback chain for the current agent: [primary, fallback1, fallback2, ...].
    /// Set before calling CompleteAsync to enable model-first routing.
    /// When set, models are tried in order, each resolving to a provider.
    /// When null, falls back to provider-priority routing (backward compat).
    /// </summary>
    public IReadOnlyList<string>? ModelChain { get; set; }

    public string EffectiveModel => ModelChain is { Count: > 0 } chain
        ? chain[0]
        : _providers.FirstOrDefault()?.Model ?? "unknown";

    public string Name => "Router";
    public string Model => "Multi";
    public bool SupportsStreaming => _providers.Any(p => p.SupportsStreaming);
    public bool SupportsTools => _providers.Any(p => p.SupportsTools);

    public ProviderRouter(
        IReadOnlyList<ILLMProvider> providers,
        ProviderRoutingOptions options,
        AetherDb db,
        ILogger<ProviderRouter> logger)
    {
        _options = options;
        _providers = providers.OrderBy(p => GetPriority(p.Name)).ToList();
        _db = db;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var complexity = EstimateComplexity(request.Messages);

        // Model-first routing: iterate agent's model chain
        if (ModelChain is { Count: > 0 })
        {
            foreach (var modelId in ModelChain)
            {
                var provider = ResolveModelToProvider(modelId);
                if (provider is null)
                {
                    _logger.LogWarning("Model '{ModelId}' could not be resolved to any provider, skipping", modelId);
                    continue;
                }

                if (IsCircuitOpen(provider.Name))
                {
                    _logger.LogDebug("Skipping {Provider} for model '{ModelId}' - circuit open", provider.Name, modelId);
                    continue;
                }

                try
                {
                    _logger.LogInformation("Trying model '{ModelId}' via {Provider} (complexity {Complexity})",
                        modelId, provider.Name, complexity);
                    var response = await provider.CompleteAsync(request, ct);
                    RecordSuccess(provider.Name);
                    await RecordUsageAsync(provider.Name, provider.Model, 0, 0, null, ct);
                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Model '{ModelId}' via {Provider} failed, trying next in chain",
                        modelId, provider.Name);
                    RecordFailure(provider.Name);
                }
            }

            throw new InvalidOperationException("All models in agent chain failed");
        }

        // Fallback: provider-priority routing (backward compat, no ModelChain set)
        var needsBetterModel = ShouldEscalateToBetterModel(request.Messages, complexity);
        var primaryGroup = PrimaryGroup;
        try
        {
            _logger.LogInformation("Trying primary group ({Group}) for complexity {Complexity}", primaryGroup, complexity);
            var response = await TryEndpointGroupAsync(primaryGroup, request, ct);
            await RecordUsageAsync(primaryGroup, "unknown", 0, 0, null, ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary group failed");
            RecordFailure(primaryGroup);
        }

        foreach (var fallbackGroup in FallbackGroups)
        {
            try
            {
                _logger.LogInformation("Trying fallback ({Group}) for complexity {Complexity}", fallbackGroup, complexity);
                var response = await TryEndpointGroupAsync(fallbackGroup, request, ct);
                await RecordUsageAsync(fallbackGroup, "unknown", 0, 0, null, ct);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback group {Group} also failed", fallbackGroup);
                RecordFailure(fallbackGroup);
            }
        }

        throw new InvalidOperationException("All provider groups failed");
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var complexity = EstimateComplexity(request.Messages);
        var endpoint = SelectEndpoint(PrimaryGroup) ?? SelectEndpoint(FallbackGroups.FirstOrDefault() ?? "openrouter");

        if (endpoint is null)
        {
            throw new InvalidOperationException("No available endpoints");
        }

        if (!endpoint.SupportsStreaming)
        {
            // Fall back to non-streaming
            var response = await endpoint.CompleteAsync(request, ct);
            yield return response.Content;
            yield break;
        }

        // For streaming, we'd need provider to expose streaming method
        // This is a placeholder - actual streaming requires provider-specific implementation
        var fullResponse = await endpoint.CompleteAsync(request, ct);
        yield return fullResponse.Content;
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var complexity = EstimateComplexity(request.Messages);
        var needsBetterModel = ShouldEscalateToBetterModel(request.Messages, complexity);

        // Try primary group first (buffer results outside try-catch because yields
        // cannot live inside try blocks with catch clauses in C#)
        var events = new List<StreamEvent>();
        var primarySucceeded = false;
        var primaryGroup = PrimaryGroup;

        try
        {
            _logger.LogInformation("Trying primary group ({Group}) for streaming (complexity {Complexity})", primaryGroup, complexity);
            var endpoint = SelectEndpoint(primaryGroup);
            if (endpoint is not null)
            {
                await foreach (var evt in endpoint.CompleteStreamingEventsAsync(request, ct))
                {
                    events.Add(evt);
                }
                primarySucceeded = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary streaming group failed");
            RecordFailure(primaryGroup);
        }

        if (primarySucceeded)
        {
            foreach (var evt in events) yield return evt;
            yield break;
        }

        foreach (var fallbackGroup in FallbackGroups)
        {
            events.Clear();
            var succeeded = false;
            try
            {
                _logger.LogInformation("Trying fallback ({Group}) for streaming", fallbackGroup);
                var endpoint = SelectEndpoint(fallbackGroup);
                if (endpoint is not null)
                {
                    await foreach (var evt in endpoint.CompleteStreamingEventsAsync(request, ct))
                    {
                        events.Add(evt);
                    }
                    succeeded = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback streaming group {Group} also failed", fallbackGroup);
                RecordFailure(fallbackGroup);
            }

            if (succeeded)
            {
                foreach (var evt in events) yield return evt;
                yield break;
            }
        }

        throw new InvalidOperationException("All provider groups failed for streaming");
    }

    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        foreach (var p in _providers)
        {
            try
            {
                if (await p.HealthCheckAsync(ct))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private ILLMProvider? SelectEndpoint(string groupName)
    {
        var endpoints = GetEndpointGroup(groupName);
        foreach (var endpoint in endpoints)
        {
            if (IsCircuitOpen(endpoint.Name))
            {
                _logger.LogDebug("Skipping {Endpoint} - circuit open", endpoint.Model);
                continue;
            }
            return endpoint;
        }
        return null;
    }

    private async Task<LlmResponse> TryEndpointGroupAsync(string groupName, LlmRequest request, CancellationToken ct)
    {
        var endpoints = GetEndpointGroup(groupName);

        foreach (var endpoint in endpoints)
        {
            if (IsCircuitOpen(endpoint.Name))
            {
                _logger.LogDebug("Skipping {Endpoint} - circuit open", endpoint.Model);
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Trying endpoint {Endpoint} in group {Group}", endpoint.Model, groupName);
                var response = await endpoint.CompleteAsync(request, ct);
                sw.Stop();

                RecordSuccess(endpoint.Name);
                await RecordUsageAsync(groupName, endpoint.Model, 0, 0, sw.ElapsedMilliseconds, ct);

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "Endpoint {Endpoint} failed, trying next", endpoint.Model);
                RecordFailure(endpoint.Name);
            }
        }

        throw new InvalidOperationException($"All endpoints in group {groupName} failed");
    }

    // === Complexity Scoring ===

    private float EstimateComplexity(IReadOnlyList<LlmMessage> messages)
    {
        var lastContent = messages.LastOrDefault()?.Content ?? "";
        var lengthScore = Math.Min(lastContent.Length / 1000f, 1.0f);
        var keywordScore = DetectComplexityKeywords(lastContent) ? 0.8f : 0.0f;
        return Math.Max(lengthScore, keywordScore);
    }

    private bool DetectComplexityKeywords(string content)
    {
        var keywords = new[] { "architecture", "design", "optimize", "refactor",
            "complex", "performance", "security", "algorithm" };
        return keywords.Any(k => content.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldEscalateToBetterModel(IReadOnlyList<LlmMessage> messages, float complexity)
    {
        return complexity > _options.ComplexityThreshold ||
               DetectComplexityKeywords(messages.LastOrDefault()?.Content ?? "");
    }

    // === Circuit Breaker ===

    private bool IsCircuitOpen(string providerName)
    {
        lock (_lock)
        {
            if (!_circuitBreakers.TryGetValue(providerName, out var state))
            {
                return false;
            }

            if (state.IsOpen && DateTime.UtcNow - state.OpenedAt > CircuitResetTimeout)
            {
                // Half-open - allow one attempt
                state.IsHalfOpen = true;
                _logger.LogInformation("Circuit for {Provider} entering half-open state", providerName);
                return false;
            }

            return state.IsOpen && !state.IsHalfOpen;
        }
    }

    private void RecordFailure(string providerName)
    {
        lock (_lock)
        {
            if (!_circuitBreakers.TryGetValue(providerName, out var state))
            {
                state = new CircuitBreakerState();
                _circuitBreakers[providerName] = state;
            }

            state.ConsecutiveFailures++;
            state.IsHalfOpen = false;

            if (state.ConsecutiveFailures >= CircuitFailureThreshold)
            {
                state.IsOpen = true;
                state.OpenedAt = DateTime.UtcNow;
                _logger.LogWarning("Circuit opened for {Provider} after {Failures} failures",
                    providerName, state.ConsecutiveFailures);
            }
        }
    }

    private void RecordSuccess(string providerName)
    {
        lock (_lock)
        {
            if (_circuitBreakers.TryGetValue(providerName, out var state))
            {
                state.ConsecutiveFailures = 0;
                state.IsOpen = false;
                state.IsHalfOpen = false;
            }
        }
    }

    // === Cost Tracking ===

    private async Task RecordUsageAsync(string provider, string model, int inputTokens, int outputTokens, long? latencyMs, CancellationToken ct)
    {
        try
        {
            var usage = new ProviderUsage(
                Id: Guid.NewGuid().ToString(),
                Provider: provider,
                Model: model,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CostUsd: EstimateCost(provider, model, inputTokens, outputTokens),
                LatencyMs: latencyMs.HasValue ? (int)latencyMs : null,
                Timestamp: DateTimeOffset.UtcNow);

            await _db.RecordProviderUsageAsync(usage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record provider usage");
        }
    }

    private static double? EstimateCost(string provider, string model, int inputTokens, int outputTokens)
    {
        // Rough cost estimates per 1M tokens
        return provider.ToLowerInvariant() switch
        {
            "fireworks" => (inputTokens * 0.15 + outputTokens * 0.6) / 1_000_000,
            "openrouter" => (inputTokens * 0.5 + outputTokens * 1.5) / 1_000_000,
            "anthropic" => (inputTokens * 3.0 + outputTokens * 15.0) / 1_000_000,
            _ => null
        };
    }

    // ── Per-Agent Resolution ──

    /// <summary>
    /// Resolve effective API key for a provider, following the chain:
    /// agent auth profile → agent spec config → environment variable.
    /// Returns null if no key is configured.
    /// </summary>
    public string? ResolveEffectiveApiKey(string providerName)
    {
        // 1. Agent spec providers section (from .aether.json merged with auth profiles)
        if (CurrentAgent?.Providers.TryGetValue(providerName, out var agentProvider) == true &&
            !string.IsNullOrEmpty(agentProvider.ApiKey))
            return agentProvider.ApiKey;

        // 2. Environment variable (AETHER_PROVIDERS_<NAME>_API_KEY)
        var envKey = Environment.GetEnvironmentVariable($"AETHER_PROVIDERS_{providerName.ToUpperInvariant()}_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
            return envKey;

        // 3. Legacy env var
        var legacyKey = Environment.GetEnvironmentVariable("AETHER_llm__api_key")
                        ?? Environment.GetEnvironmentVariable("AETHER_llm_api_key");
        return legacyKey;
    }

    /// <summary>
    /// Resolve effective model for a provider from agent config.
    /// Falls back through: agent primary model → agent fallback chain → provider default.
    /// </summary>
    public string ResolveEffectiveModel(string providerName, string providerDefaultModel)
    {
        if (CurrentAgent?.Providers.TryGetValue(providerName, out var agentProvider) == true &&
            !string.IsNullOrEmpty(agentProvider.Model))
            return agentProvider.Model;

        return providerDefaultModel;
    }

    /// <summary>
    /// Get the model fallback chain for the current agent.
    /// Returns [primary, fallback1, fallback2, ...] for the specified provider,
    /// or just [providerDefault] if no agent-specific config exists.
    /// </summary>
    public IReadOnlyList<string> GetModelFallbackChain(string providerName, string providerDefaultModel)
    {
        var chain = new List<string>();

        if (CurrentAgent?.Providers.TryGetValue(providerName, out var agentProvider) == true)
        {
            if (!string.IsNullOrEmpty(agentProvider.Model))
                chain.Add(agentProvider.Model);
        }

        if (chain.Count == 0)
            chain.Add(providerDefaultModel);

        return chain;
    }

    /// <summary>
    /// Resolve per-model parameter overrides from agent config.
    /// Returns the provider entry with agent-specific maxTokens/temperature or null.
    /// </summary>
    public SpecProviderEntry? GetAgentProviderConfig(string providerName)
    {
        if (CurrentAgent?.Providers.TryGetValue(providerName, out var agentProvider) == true)
            return agentProvider;
        return null;
    }

    private IReadOnlyList<ILLMProvider> GetEndpointGroup(string providerName) =>
        _providers.Where(p => p.Name.StartsWith(providerName, StringComparison.OrdinalIgnoreCase)).ToList();

    private int GetPriority(string providerName) =>
        _options.ProviderPriorities.GetValueOrDefault(providerName.Split('-')[0], 999);

    // ── Model-to-Provider Resolution ──

    /// <summary>
    /// Resolve a model identifier to the best-matching ILLMProvider.
    /// Resolution order:
    ///   1. Exact match in any provider's Models list
    ///   2. Exact match against any provider's default Model
    ///   3. Prefix match: modelId starts with "providerName/"
    ///   4. Returns null if no provider matches
    /// </summary>
    public ILLMProvider? ResolveModelToProvider(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return null;

        // 1. Check provider Models list (explicit model support)
        foreach (var provider in _providers)
        {
            var cfg = GetAgentProviderConfig(provider.Name);
            if (cfg?.Models is { Count: > 0 } && cfg.Models.Contains(modelId, StringComparer.OrdinalIgnoreCase))
                return provider;
        }

        // 2. Check each provider's default model
        foreach (var provider in _providers)
        {
            var cfg = GetAgentProviderConfig(provider.Name);
            if (cfg is not null && string.Equals(cfg.Model, modelId, StringComparison.OrdinalIgnoreCase))
                return provider;
            // Also check the provider instance's model
            if (string.Equals(provider.Model, modelId, StringComparison.OrdinalIgnoreCase))
                return provider;
        }

        // 3. Prefix match: "openrouter/deepseek/r1" → provider named "openrouter"
        var slashIdx = modelId.IndexOf('/');
        if (slashIdx > 0)
        {
            var prefix = modelId[..slashIdx];
            var match = _providers.FirstOrDefault(p =>
                p.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return null;
    }

    private sealed class CircuitBreakerState
    {
        public int ConsecutiveFailures { get; set; }
        public bool IsOpen { get; set; }
        public bool IsHalfOpen { get; set; }
        public DateTime OpenedAt { get; set; }
    }
}

public class ProviderRoutingOptions
{
    public float ComplexityThreshold { get; set; } = 0.8f;
    public float ConfidenceThreshold { get; set; } = 0.6f;
    public int MaxRetries { get; set; } = 2;
    public Dictionary<string, int> ProviderPriorities { get; set; } = new();
    public List<string> SafetyProviders { get; set; } = new() { "anthropic" };
}