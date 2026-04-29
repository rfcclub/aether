namespace Aether.Agents;

/// <summary>
/// FEOFALLS boot retrieval contract. Loads cognitive layers in order at session start.
/// Constitution → Identity → Cognitive → Working State.
/// </summary>
public interface IBootContract
{
    Task<string> LoadConstitutionAsync(CancellationToken ct = default);
    Task<string> LoadIdentityAsync(CancellationToken ct = default);
    Task<string> LoadCognitiveAsync(CancellationToken ct = default);
    Task<string> LoadWorkingStateAsync(CancellationToken ct = default);
}
