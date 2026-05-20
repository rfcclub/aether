using Aether.Agent;
using Aether.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Scheduling;

public class ProactiveTaskService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ProactiveTaskService> _logger;

    public ProactiveTaskService(IServiceProvider services, ILogger<ProactiveTaskService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProactiveTaskService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckIdleSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProactiveTaskService.");
            }

            // Check every 10 minutes
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }

    private async Task CheckIdleSessionsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();
        
        // Find sessions idle for more than 4 hours but less than 5 hours (so we only trigger once)
        var recentSessions = await sessionManager.GetRecentSessionsAsync(100, ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var session in recentSessions)
        {
            var idleTime = now - session.LastActivity;
            if (idleTime.TotalHours >= 4 && idleTime.TotalHours < 4.2)
            {
                _logger.LogInformation("Session {SessionId} idle for {Hours} hours. Triggering proactive reflection.", session.Id, idleTime.TotalHours);
                await TriggerReflectionAsync(scope.ServiceProvider, session, ct);
            }
        }
    }

    private async Task TriggerReflectionAsync(IServiceProvider provider, Session session, CancellationToken ct)
    {
        var soul = provider.GetRequiredService<AetherSoul>();
        var prompt = "[System: Proactive Loop] You have been idle for 4 hours. Review your active goals in the GoalStore and perform any necessary reflection, planning, or autonomous tasks.";
        
        // Use ProcessTaskAsync to execute silently without emitting to UI channels directly unless intended
        await soul.ProcessTaskAsync(session.GroupFolder, prompt, ct);
    }
}
