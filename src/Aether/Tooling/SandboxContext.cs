using Aether.Config;

namespace Aether.Tooling;

public sealed class SandboxContext : ISandboxContext
{
    public string WorkspacePath { get; }
    public bool AllowWrites { get; }
    public IReadOnlyList<string> AllowedPaths { get; }
    public IReadOnlyList<string> DeniedPaths { get; }
    public IReadOnlyList<string> AllowedCommands { get; }
    public int BashTimeoutSeconds { get; }

    public SandboxContext(string workspacePath, SpecToolsSection? tools = null)
    {
        WorkspacePath = workspacePath;

        if (tools is not null)
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
            AllowWrites = false;
            AllowedPaths = new[] { workspacePath };
            DeniedPaths = Array.Empty<string>();
            AllowedCommands = Array.Empty<string>();
            BashTimeoutSeconds = 60;
        }
    }

    public bool IsPathAllowed(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var resolved = Path.GetFullPath(path);

        // Check denied paths first
        foreach (var denied in DeniedPaths)
        {
            if (resolved.StartsWith(Path.GetFullPath(denied), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check allowed paths
        foreach (var allowed in AllowedPaths)
        {
            if (resolved.StartsWith(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
