namespace Aether.Memory;

public sealed class FileMemory : IMemorySystem
{
    private readonly string _groupsRoot;

    public FileMemory(string groupsRoot)
    {
        _groupsRoot = groupsRoot;
    }

    public async Task<string> LoadContextAsync(string groupFolder, CancellationToken ct)
    {
        var parts = new List<string>();

        await AddIfExistsAsync(parts, Path.Combine(_groupsRoot, "CLAUDE.md"), "Global", ct);
        await AddIfExistsAsync(parts, Path.Combine(_groupsRoot, groupFolder, "CLAUDE.md"), groupFolder, ct);

        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static async Task AddIfExistsAsync(List<string> parts, string path, string label, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        if (!string.IsNullOrWhiteSpace(content))
        {
            parts.Add($"# {label} Context{Environment.NewLine}{content.Trim()}");
        }
    }
}
