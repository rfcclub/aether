using Aether.Config;

namespace Aether.Tooling;

public class SandboxContext
{
    private readonly bool _disabled;

    public string WorkspacePath { get; }
    public bool AllowWrites { get; }
    public IReadOnlyList<string> AllowedPaths { get; }
    public IReadOnlyList<string> DeniedPaths { get; }
    public IReadOnlyList<string> AllowedCommands { get; }
    public int BashTimeoutSeconds { get; }

    public SandboxContext(string workspacePath, SpecToolsSection? tools = null, string sandboxType = "process")
    {
        WorkspacePath = workspacePath;
        _disabled = string.Equals(sandboxType, "none", StringComparison.OrdinalIgnoreCase);

        if (_disabled)
        {
            AllowWrites = true;
            AllowedPaths = Array.Empty<string>();
            DeniedPaths = Array.Empty<string>();
            AllowedCommands = Array.Empty<string>();
            BashTimeoutSeconds = 60;
        }
        else if (tools is not null)
        {
            AllowWrites = tools.File.AllowWrites;
            AllowedPaths = tools.File.AllowedPaths.Count > 0
                ? tools.File.AllowedPaths
                : new[] { workspacePath };
            DeniedPaths = tools.File.DeniedPaths;
            AllowedCommands = tools.Shell.AllowedCommands;
            BashTimeoutSeconds = tools.Shell.TimeoutSeconds > 0 ? tools.Shell.TimeoutSeconds : 60;
        }
        else
        {
            AllowWrites = true;
            AllowedPaths = new[] { workspacePath };
            DeniedPaths = Array.Empty<string>();
            AllowedCommands = Array.Empty<string>();
            BashTimeoutSeconds = 60;
        }
    }

    public bool IsPathAllowed(string path)
    {
        if (_disabled) return true;
        if (string.IsNullOrEmpty(path)) return false;

        var resolved = Path.GetFullPath(path);

        foreach (var denied in DeniedPaths)
        {
            if (resolved.StartsWith(Path.GetFullPath(denied), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var allowed in AllowedPaths)
        {
            if (resolved.StartsWith(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return AllowedPaths.Count == 0;
    }
}
