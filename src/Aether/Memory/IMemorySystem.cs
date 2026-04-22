namespace Aether.Memory;

public interface IMemorySystem
{
    Task<string> LoadContextAsync(string groupFolder, CancellationToken ct);
}
