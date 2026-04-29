namespace Aether.Agents;

public enum FeofallsLayer { Constitution, Identity, Cognitive, Learning, OperationalData, WorkingState, Unknown }

public readonly record struct WriteResult(bool Allowed, bool RequiresApproval);

/// <summary>
/// Enforces FEOFALLS write-boundary rules:
/// - 0_CONSTITUTION and 1_IDENTITY: creator approval required
/// - 3_LEARNING and 5_WORKING_STATE: auto-approved
/// </summary>
public sealed class WriteValidator
{
    private readonly FeofallsConfig _config;

    public WriteValidator(FeofallsConfig config)
    {
        _config = config;
    }

    public WriteResult ValidateWrite(string relativePath, FeofallsLayer layer)
    {
        return layer switch
        {
            FeofallsLayer.Constitution => new WriteResult(false, true),
            FeofallsLayer.Identity => new WriteResult(false, true),
            _ => new WriteResult(true, false)
        };
    }

    public WriteResult ValidateRead(string relativePath, FeofallsLayer layer)
    {
        return new WriteResult(true, false);
    }

    public static FeofallsLayer ClassifyPath(string relativePath, FeofallsConfig config)
    {
        var fileName = Path.GetFileName(relativePath);
        if (config.ConstitutionFiles.Any(f => Path.GetFileName(f) == fileName))
            return FeofallsLayer.Constitution;
        if (config.IdentityFiles.Any(f => Path.GetFileName(f) == fileName))
            return FeofallsLayer.Identity;
        if (config.CognitiveFiles.Any(f => Path.GetFileName(f) == fileName))
            return FeofallsLayer.Cognitive;
        if (fileName == Path.GetFileName(config.EpisodicLogFile) ||
            fileName == Path.GetFileName(config.MistakesFile) ||
            fileName == Path.GetFileName(config.DreamsFile))
            return FeofallsLayer.Learning;
        if (fileName == Path.GetFileName(config.TaskInboxFile ?? "") ||
            fileName == Path.GetFileName(config.TaskReportFile ?? "") ||
            fileName == Path.GetFileName(config.HeartbeatFile ?? ""))
            return FeofallsLayer.WorkingState;
        return FeofallsLayer.Unknown;
    }
}
