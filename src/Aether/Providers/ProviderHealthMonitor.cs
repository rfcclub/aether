using Microsoft.Extensions.Logging;

namespace Aether.Providers;

/// <summary>
/// Provider health monitor - runs periodic health checks on all registered providers.
/// Integrates with circuit breaker to skip unhealthy providers.
/// </summary>
public sealed class ProviderHealthMonitor : IHostedService, IDisposable
{
    private readonly IReadOnlyList<ILLMProvider> _providers;
    private readonly ILogger<ProviderHealthMonitor> _logger;
    private readonly Dictionary<string, ProviderHealthState> _healthStates = new();
    private readonly object _lock = new();
    private Timer? _timer;
    private const int CheckIntervalSeconds = 30;
    private const int FailureThreshold = 3;

    public ProviderHealthMonitor(
        IReadOnlyList<ILLMProvider> providers,
        ILogger<ProviderHealthMonitor> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("ProviderHealthMonitor starting with {Count} providers", _providers.Count);
        _timer = new Timer(CheckAllProviders, null, TimeSpan.Zero, TimeSpan.FromSeconds(CheckIntervalSeconds));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private async void CheckAllProviders(object? state)
    {
        foreach (var provider in _providers)
        {
            await CheckProviderAsync(provider);
        }
    }

    private async Task CheckProviderAsync(ILLMProvider provider)
    {
        try
        {
            var isHealthy = await provider.HealthCheckAsync();

            lock (_lock)
            {
                if (!_healthStates.TryGetValue(provider.Name, out var healthState))
                {
                    healthState = new ProviderHealthState();
                    _healthStates[provider.Name] = healthState;
                }

                if (isHealthy)
                {
                    if (healthState.ConsecutiveFailures > 0)
                    {
                        _logger.LogInformation("Provider {Name} recovered (failures reset)", provider.Name);
                    }
                    healthState.ConsecutiveFailures = 0;
                    healthState.Status = ProviderStatus.Healthy;
                }
                else
                {
                    healthState.ConsecutiveFailures++;
                    if (healthState.ConsecutiveFailures >= FailureThreshold)
                    {
                        healthState.Status = ProviderStatus.Degraded;
                        _logger.LogWarning("Provider {Name} marked Degraded after {Failures} consecutive failures",
                            provider.Name, healthState.ConsecutiveFailures);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed for provider {Name}", provider.Name);

            lock (_lock)
            {
                if (!_healthStates.TryGetValue(provider.Name, out var healthState))
                {
                    healthState = new ProviderHealthState();
                    _healthStates[provider.Name] = healthState;
                }

                healthState.ConsecutiveFailures++;
                if (healthState.ConsecutiveFailures >= FailureThreshold)
                {
                    healthState.Status = ProviderStatus.Degraded;
                }
            }
        }
    }

    public ProviderHealthState? GetHealthState(string providerName)
    {
        lock (_lock)
        {
            return _healthStates.GetValueOrDefault(providerName);
        }
    }

    public bool IsHealthy(string providerName)
    {
        lock (_lock)
        {
            if (!_healthStates.TryGetValue(providerName, out var state))
            {
                return true; // Unknown = assume healthy
            }
            return state.Status == ProviderStatus.Healthy;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

public enum ProviderStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

public sealed class ProviderHealthState
{
    public ProviderStatus Status { get; set; } = ProviderStatus.Unknown;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.UtcNow;
}