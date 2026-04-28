using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.SelfImprovement;

public class DailyReviewHostedService : BackgroundService
{
    private readonly ISelfImprovementService _pipeline;
    private readonly ILogger<DailyReviewHostedService> _logger;
    private DateTime _lastFired;

    public DailyReviewHostedService(
        ISelfImprovementService pipeline,
        ILogger<DailyReviewHostedService> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
        _lastFired = DateTime.MinValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailyReviewHostedService started, polling for midnight UTC");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Check if we're in the midnight window (00:00-00:01 UTC) and haven't fired today
                if (now.Hour == 0 && now.Minute <= 1 && _lastFired.Date < now.Date)
                {
                    _lastFired = now;
                    var startedAt = DateTime.UtcNow;
                    _logger.LogInformation("Midnight window hit, starting daily review");

                    try
                    {
                        await _pipeline.RunDailyReviewAsync(stoppingToken);
                        var duration = DateTime.UtcNow - startedAt;
                        _logger.LogInformation("Daily review completed in {Duration}", duration);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Daily review failed with exception");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyReviewHostedService loop exception");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
