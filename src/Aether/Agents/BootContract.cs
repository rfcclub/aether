using System.Text;

namespace Aether.Agents;

public sealed class BootContract 
{
    private readonly string _agentDir;
    private readonly BootConfig _config;

    public BootContract(string agentDir, BootConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    public async Task<string> LoadConstitutionAsync(CancellationToken ct = default) =>
        await LoadFilesAsync(_config.ConstitutionFiles, ct);

    public async Task<string> LoadIdentityAsync(CancellationToken ct = default) =>
        await LoadFilesAsync(_config.IdentityFiles, ct);

    public async Task<string> LoadCognitiveAsync(CancellationToken ct = default) =>
        await LoadFilesAsync(_config.CognitiveFiles, ct);

    public async Task<string> LoadWorkingStateAsync(CancellationToken ct = default)
    {
        var files = new List<string>();
        if (_config.TaskInboxFile is not null) files.Add(_config.TaskInboxFile);
        if (_config.HeartbeatFile is not null) files.Add(_config.HeartbeatFile);
        return await LoadFilesAsync(files, ct);
    }

    private async Task<string> LoadFilesAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(_agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(await File.ReadAllTextAsync(fullPath, ct));
        }
        return sb.ToString();
    }
}
