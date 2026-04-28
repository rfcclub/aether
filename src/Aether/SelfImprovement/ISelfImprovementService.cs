namespace Aether.SelfImprovement;

public interface ISelfImprovementService
{
    Task RunDailyReviewAsync(CancellationToken ct = default);
}
