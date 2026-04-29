using Aether.Agent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Agents;

/// <summary>
/// Periodic heartbeat service that reads HEARTBEAT.md and sends its content
/// through AetherSoul for processing. Implements the OC heartbeat pattern:
/// poll → execute tasks → report.
/// </summary>
public sealed class AgentHeartbeatService : IHostedService, IDisposable
{
    private readonly IAgentProfile _profile;
    private readonly AetherSoul _soul;
    private readonly AgentConfig _config;
    private readonly ILogger<AgentHeartbeatService> _logger;
    private readonly TimeSpan _interval;
    private Timer? _timer;
    private CancellationTokenSource? _cts;

    public AgentHeartbeatService(
        IAgentProfile profile,
        AetherSoul soul,
        AgentConfig config,
        ILogger<AgentHeartbeatService> logger,
        TimeSpan? interval = null)
    {
        _profile = profile;
        _soul = soul;
        _config = config;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config.HeartbeatFile is null)
        {
            _logger.LogInformation("Heartbeat disabled for agent {AgentName}", _profile.Name);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Heartbeat starting for agent {AgentName} every {Interval}",
            _profile.Name, _interval);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new Timer(async _ => await TickAsync(_cts.Token), null,
            TimeSpan.Zero, _interval);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Heartbeat stopping for agent {AgentName}", _profile.Name);
        _timer?.Change(Timeout.Infinite, 0);
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var heartbeatContent = await _profile.LoadFileAsync(_config.HeartbeatFile!, ct);
            if (heartbeatContent is null)
            {
                _logger.LogDebug("No heartbeat file found for {AgentName}", _profile.Name);
                return;
            }

            _logger.LogDebug("Heartbeat tick for {AgentName}", _profile.Name);
            var response = await _soul.ProcessAsync(_profile.Name, heartbeatContent, ct);

            if (!response.Content.Contains("HEARTBEAT_OK"))
            {
                _logger.LogInformation("Heartbeat produced actionable output for {AgentName}", _profile.Name);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat tick failed for {AgentName}", _profile.Name);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
