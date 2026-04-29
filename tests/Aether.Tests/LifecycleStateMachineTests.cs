using Aether.Agents;

namespace Aether.Tests;

public sealed class LifecycleStateMachineTests
{
    [Fact]
    public void InitialState_IsActive()
    {
        var state = MemoryLifecycleState.Active;
        Assert.Equal(MemoryLifecycleState.Active, state);
    }

    [Fact]
    public void Active_DecaysAfterThreshold_WithoutAccess()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig
        {
            ActiveToDecayingDays = 60,
            DecayingToArchivedDays = 90
        });

        var createdAt = DateTime.UtcNow.AddDays(-61);
        var lastAccessed = DateTime.UtcNow.AddDays(-61);

        var state = fsm.ComputeState(createdAt, lastAccessed, accessCount: 0);

        Assert.Equal(MemoryLifecycleState.Decaying, state);
    }

    [Fact]
    public void Active_StaysActive_WithRecentAccess()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig());

        var createdAt = DateTime.UtcNow.AddDays(-100);
        var lastAccessed = DateTime.UtcNow.AddDays(-10);

        var state = fsm.ComputeState(createdAt, lastAccessed, accessCount: 5);

        Assert.Equal(MemoryLifecycleState.Active, state);
    }

    [Fact]
    public void Decaying_ArchivesAfterThreshold()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig
        {
            ActiveToDecayingDays = 30,
            DecayingToArchivedDays = 60
        });

        var createdAt = DateTime.UtcNow.AddDays(-100);
        var lastAccessed = DateTime.UtcNow.AddDays(-61);

        var state = fsm.ComputeState(createdAt, lastAccessed, accessCount: 1);

        Assert.Equal(MemoryLifecycleState.Archived, state);
    }

    [Fact]
    public void SalienceScore_DecaysWithLogFormula()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig());

        var score = fsm.ComputeSalience(accessCount: 10, lastAccessAge: TimeSpan.FromDays(5));

        Assert.True(score > 0);
        Assert.True(score <= 1.0);
    }
}
