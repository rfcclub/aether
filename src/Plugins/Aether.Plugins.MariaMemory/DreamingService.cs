using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class DreamingService
{
    private readonly ILogger _logger;
    private readonly MariaMemoryStore _store;
    private readonly AutoPromotionEngine _promoter;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;

    public DreamingService(MariaMemoryStore store, AutoPromotionEngine promoter, ILogger logger)
    {
        _store = store;
        _promoter = promoter;
        _logger = logger;
    }

    public void StartBackground(TimeSpan interval)
    {
        _cts = new CancellationTokenSource();
        _backgroundTask = Task.Run(() => BackgroundLoopAsync(interval, _cts.Token));
        _logger.LogInformation("Dreaming background task started with interval {Interval}", interval);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task BackgroundLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for the interval or until cancelled
                await Task.Delay(interval, ct);
                await PerformDreamCycleAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dreaming background loop");
            }
        }
    }

    public async Task PerformDreamCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("Initiating Dreaming Cycle...");

        // 1. Light Dreaming (Metadata & Indexing)
        await LightDreamingAsync(ct);

        // 2. REM Dreaming (Pattern Detection)
        await RemDreamingAsync(ct);

        // 3. Deep Dreaming (Consolidation & Archival)
        await DeepDreamingAsync(ct);

        _logger.LogInformation("Dreaming Cycle Complete. Memory is stabilized.");
    }

    private async Task LightDreamingAsync(CancellationToken ct)
    {
        _logger.LogInformation("[Light Dream] Syncing scores...");
        await _promoter.RecalculateAllScoresAsync(ct);
    }

    private async Task RemDreamingAsync(CancellationToken ct)
    {
        _logger.LogInformation("[REM Dream] Detecting patterns...");
        // Pattern detection could involve grouping by thread_id or keywords
        // For now, we'll just log progress.
    }

    private async Task DeepDreamingAsync(CancellationToken ct)
    {
        _logger.LogInformation("[Deep Dream] Processing promotions...");
        await _promoter.ProcessPromotionAsync(ct);
    }
}
