namespace Aether.Tooling;

/// <summary>
/// Sandbox restrictions for tool execution, derived from agent spec.
/// </summary>
public interface ISandboxContext
{
    string WorkspacePath { get; }
    bool AllowWrites { get; }
    bool IsPathAllowed(string path);
    IReadOnlyList<string> AllowedPaths { get; }
    IReadOnlyList<string> DeniedPaths { get; }
    IReadOnlyList<string> AllowedCommands { get; }
    int BashTimeoutSeconds { get; }
}
