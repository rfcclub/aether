namespace Aether.Agents;

public enum BootLayer { Constitution, Identity, Cognitive, Learning, OperationalData, WorkingState, Unknown }

public readonly record struct WriteResult(bool Allowed, bool RequiresApproval);

/// <summary>
/// Enforces FEOFALLS write-boundary rules:
/// - 0_CONSTITUTION and 1_IDENTITY: creator approval required
/// - 3_LEARNING and 5_WORKING_STATE: auto-approved
/// </summary>
public sealed class WriteValidator
{
    private readonly BootConfig _config;

    public WriteValidator(BootConfig config)
    {
        _config = config;
    }

    public WriteResult ValidateWrite(string relativePath, BootLayer layer)
    {
        return layer switch
        {
            BootLayer.Constitution => new WriteResult(false, true),
            BootLayer.Identity => new WriteResult(false, true),
            _ => new WriteResult(true, false)
        };
    }

    public WriteResult ValidateRead(string relativePath, BootLayer layer)
    {
        return new WriteResult(true, false);
    }

    public static BootLayer ClassifyPath(string relativePath, BootConfig config)
    {
        var fileName = Path.GetFileName(relativePath);
        if (config.ConstitutionFiles.Any(f => Path.GetFileName(f) == fileName))
            return BootLayer.Constitution;
        if (config.IdentityFiles.Any(f => Path.GetFileName(f) == fileName))
            return BootLayer.Identity;
        if (config.CognitiveFiles.Any(f => Path.GetFileName(f) == fileName))
            return BootLayer.Cognitive;
        if (fileName == Path.GetFileName(config.EpisodicLogFile) ||
            fileName == Path.GetFileName(config.MistakesFile) ||
            fileName == Path.GetFileName(config.DreamsFile))
            return BootLayer.Learning;
        if (fileName == Path.GetFileName(config.TaskInboxFile ?? "") ||
            fileName == Path.GetFileName(config.TaskReportFile ?? "") ||
            fileName == Path.GetFileName(config.HeartbeatFile ?? ""))
            return BootLayer.WorkingState;
        return BootLayer.Unknown;
    }
}
