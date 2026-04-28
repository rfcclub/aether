using System.Diagnostics;
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
/// </summary>
public class ProviderRouter : ILLMProvider
{
    private readonly IReadOnlyList<ILLMProvider> _providers;
    private readonly ProviderRoutingOptions _options;
    private readonly AetherDb _db;
    private readonly ILogger<ProviderRouter> _logger;
    private readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();
    private readonly object _lock = new();

    private const int CircuitFailureThreshold = 3;
    private static readonly TimeSpan CircuitResetTimeout = TimeSpan.FromSeconds(60);

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
        var needsBetterModel = ShouldEscalateToBetterModel(request.Messages, complexity);

        // Try primary group first
        try
        {
            _logger.LogInformation("Trying primary group (fireworks) for complexity {Complexity}", complexity);
            var response = await TryEndpointGroupAsync("fireworks", request, ct);

            // Record usage (simplified - no token counting without provider response)
            await RecordUsageAsync("fireworks", "unknown", 0, 0, null, ct);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary group failed");
            RecordFailure("fireworks");
        }

        if (needsBetterModel)
        {
            try
            {
                _logger.LogInformation("Escalating to fallback (openrouter) for complexity {Complexity}", complexity);
                var response = await TryEndpointGroupAsync("openrouter", request, ct);

                await RecordUsageAsync("openrouter", "unknown", 0, 0, null, ct);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallback group also failed");
                RecordFailure("openrouter");
            }
        }

        throw new InvalidOperationException("All provider groups failed");
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var complexity = EstimateComplexity(request.Messages);
        var endpoint = SelectEndpoint("fireworks") ?? SelectEndpoint("openrouter");

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

        try
        {
            _logger.LogInformation("Trying primary group (fireworks) for streaming (complexity {Complexity})", complexity);
            var endpoint = SelectEndpoint("fireworks");
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
            RecordFailure("fireworks");
        }

        if (primarySucceeded)
        {
            foreach (var evt in events) yield return evt;
            yield break;
        }

        if (needsBetterModel)
        {
            events.Clear();
            var fallbackSucceeded = false;
            try
            {
                _logger.LogInformation("Escalating to fallback (openrouter) for streaming");
                var endpoint = SelectEndpoint("openrouter");
                if (endpoint is not null)
                {
                    await foreach (var evt in endpoint.CompleteStreamingEventsAsync(request, ct))
                    {
                        events.Add(evt);
                    }
                    fallbackSucceeded = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallback streaming group also failed");
                RecordFailure("openrouter");
            }

            if (fallbackSucceeded)
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

    private IReadOnlyList<ILLMProvider> GetEndpointGroup(string providerName) =>
        _providers.Where(p => p.Name.StartsWith(providerName, StringComparison.OrdinalIgnoreCase)).ToList();

    private int GetPriority(string providerName) =>
        _options.ProviderPriorities.GetValueOrDefault(providerName.Split('-')[0], 999);

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