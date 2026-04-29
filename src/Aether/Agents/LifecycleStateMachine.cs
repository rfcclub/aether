namespace Aether.Agents;

public enum MemoryLifecycleState { Active, Decaying, Archived, Consolidated }

/// <summary>
/// FEOFALLS memory lifecycle: ACTIVE → DECAYING → ARCHIVED → CONSOLIDATED.
/// Salience decays as log(access_count + 1) over time.
/// </summary>
public sealed class LifecycleStateMachine
{
    private readonly FeofallsConfig _config;

    public LifecycleStateMachine(FeofallsConfig config)
    {
        _config = config;
    }

    public MemoryLifecycleState ComputeState(
        DateTime createdAt,
        DateTime lastAccessed,
        int accessCount)
    {
        var sinceAccess = DateTime.UtcNow - lastAccessed;

        if (sinceAccess.TotalDays > _config.DecayingToArchivedDays)
            return MemoryLifecycleState.Archived;

        if (sinceAccess.TotalDays > _config.ActiveToDecayingDays && accessCount < 2)
            return MemoryLifecycleState.Decaying;

        return MemoryLifecycleState.Active;
    }

    public double ComputeSalience(int accessCount, TimeSpan lastAccessAge)
    {
        var rawScore = Math.Log2(accessCount + 1) / Math.Log2(100);
        var decayDays = Math.Max(0, lastAccessAge.TotalDays);
        var decay = Math.Exp(-decayDays / 90.0);
        return Math.Clamp(rawScore * decay, 0.0, 1.0);
    }
}
