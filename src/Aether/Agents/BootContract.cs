using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Agents;

public sealed class BootContract
{
    private readonly string _agentDir;
    private readonly BootConfig _config;
    private readonly ILogger<BootContract> _logger;
    private bool _deprecationLogged;

    public BootContract(string agentDir, BootConfig config, ILogger<BootContract>? logger = null)
    {
        _agentDir = agentDir;
        _config = config;
        _logger = logger ?? NullLogger<BootContract>.Instance;
    }

    [Obsolete("Use ContextAssembler.AssembleIdentityContextAsync() instead.")]
    public async Task<string> LoadConstitutionAsync(CancellationToken ct = default)
    {
        LogDeprecationOnce();
        return await LoadFilesAsync(_config.ConstitutionFiles, ct);
    }

    [Obsolete("Use ContextAssembler.AssembleIdentityContextAsync() instead.")]
    public async Task<string> LoadIdentityAsync(CancellationToken ct = default)
    {
        LogDeprecationOnce();
        return await LoadFilesAsync(_config.IdentityFiles, ct);
    }

    [Obsolete("Use ContextAssembler.AssembleIdentityContextAsync() instead.")]
    public async Task<string> LoadCognitiveAsync(CancellationToken ct = default)
    {
        LogDeprecationOnce();
        return await LoadFilesAsync(_config.CognitiveFiles, ct);
    }

    public string? LoadWorkingState()
    {
        var files = new List<string>();
        if (_config.TaskInboxFile is not null) files.Add(_config.TaskInboxFile);
        if (_config.HeartbeatFile is not null) files.Add(_config.HeartbeatFile);
        return LoadFilesSync(files);
    }

    public async Task<string> LoadWorkingStateAsync(CancellationToken ct = default)
        => LoadWorkingState() ?? "";

    public async Task<string> LoadFilesAsync(IReadOnlyList<string> paths, CancellationToken ct)
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

    private string? LoadFilesSync(IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(_agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(File.ReadAllText(fullPath));
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private void LogDeprecationOnce()
    {
        if (_deprecationLogged) return;
        _deprecationLogged = true;

        var hasLegacy = _config.ConstitutionFiles.Count > 1
            || _config.IdentityFiles.Count > 3
            || _config.CognitiveFiles.Count > 1;
        if (hasLegacy)
        {
            _logger.LogWarning(
                "BootConfig.ConstitutionFiles, IdentityFiles, CognitiveFiles are deprecated. " +
                "Files are still loaded via backward compatibility. " +
                "Migrate to dynamic file discovery via ContextAssembler.");
        }
    }
}
