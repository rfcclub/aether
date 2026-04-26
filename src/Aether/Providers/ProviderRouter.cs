using Microsoft.Extensions.Logging;

namespace Aether.Providers;

/// <summary>
/// Intelligent provider routing with fallback chain.
///
/// STRATEGY:
///   1. Try primary provider (Fireworks/unlimited)
///   2. If confidence low or complex task → fallback tier
///   3. Track cost, latency, success rate per provider
///
/// TODO: Implement complexity scoring, confidence estimation,
/// circuit breaker, and cost tracking.
/// </summary>
public class ProviderRouter : ILLMProvider
{
    private readonly IReadOnlyList<ILLMProvider> _providers;
    private readonly ProviderRoutingOptions _options;
    private readonly ILogger<ProviderRouter> _logger;

    public string Name => "Router";
    public string Model => "Multi";
    public bool SupportsStreaming => false;
    public bool SupportsTools => _providers.Any(p => p.SupportsTools);

    public ProviderRouter(
        IReadOnlyList<ILLMProvider> providers,
        ProviderRoutingOptions options,
        ILogger<ProviderRouter> logger)
    {
        _providers = providers.OrderBy(p => GetPriority(p.Name)).ToList();
        _options = options;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var complexity = EstimateComplexity(request.Messages);
        var needsBetterModel = ShouldEscalateToBetterModel(request.Messages, complexity);

        try
        {
            _logger.LogInformation("Trying primary group (fireworks) for complexity {Complexity}", complexity);
            return await TryEndpointGroupAsync("fireworks", request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary group failed");
        }

        if (needsBetterModel)
        {
            try
            {
                _logger.LogInformation("Escalating to fallback (openrouter) for complexity {Complexity}", complexity);
                return await TryEndpointGroupAsync("openrouter", request, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallback group also failed");
            }
        }

        throw new InvalidOperationException("All provider groups failed");
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        var checks = _providers.Select(p => p.HealthCheckAsync(ct));
        return Task.WhenAll(checks).ContinueWith(t => t.Result.Any(r => r));
    }

    private IReadOnlyList<ILLMProvider> GetEndpointGroup(string providerName) =>
        _providers.Where(p => p.Name.StartsWith(providerName)).ToList();

    private async Task<LlmResponse> TryEndpointGroupAsync(string groupName, LlmRequest request, CancellationToken ct)
    {
        var endpoints = GetEndpointGroup(groupName);

        foreach (var endpoint in endpoints)
        {
            try
            {
                _logger.LogDebug("Trying endpoint {Endpoint} in group {Group}", endpoint.Model, groupName);
                return await endpoint.CompleteAsync(request, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Endpoint {Endpoint} failed, trying next", endpoint.Model);
            }
        }

        throw new InvalidOperationException($"All endpoints in group {groupName} failed");
    }

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

    private int GetPriority(string providerName) =>
        _options.ProviderPriorities.GetValueOrDefault(providerName.Split('-')[0], 999);
}

public class ProviderRoutingOptions
{
    public float ComplexityThreshold { get; set; } = 0.8f;
    public float ConfidenceThreshold { get; set; } = 0.6f;
    public int MaxRetries { get; set; } = 2;
    public Dictionary<string, int> ProviderPriorities { get; set; } = new();
    public List<string> SafetyProviders { get; set; } = new() { "anthropic" };
}
