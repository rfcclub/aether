using System.Text;

namespace Aether.Agent;

public sealed class ContextAssembler
{
    private static readonly Dictionary<string, int> BootstrapFileOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AGENTS.md"] = 10,
        ["SOUL.md"] = 20,
        ["IDENTITY.md"] = 30,
        ["USER.md"] = 40,
        ["MEMORY.md"] = 50,
        ["HEARTBEAT.md"] = 60,
    };

    private static readonly HashSet<string> BootstrapFiles =
        new(BootstrapFileOrder.Keys, StringComparer.OrdinalIgnoreCase);

    // Only these directories are listed in workspace contents
    private static readonly HashSet<string> AllowedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "memory", "skills" };

    // Memory files older than this many days are not listed
    private const int MemoryLookbackDays = 2;

    private readonly int _dynamicTokenBudget;

    public ContextAssembler(int dynamicTokenBudget = 4000)
    {
        _dynamicTokenBudget = dynamicTokenBudget;
    }

    public string AssembleIdentityContext(string agentDir)
    {
        var files = DiscoverBootstrapFiles(agentDir);
        if (files.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Project Context");
        sb.AppendLine();
        sb.AppendLine("The following files define your identity, behavior, and memory.");
        sb.AppendLine("They are loaded by Aether and included below. Embody them fully.");
        sb.AppendLine();

        foreach (var (path, content) in files)
        {
            sb.AppendLine($"### {path}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        // Append workspace directory listing so the LLM knows what additional files exist
        var listing = DiscoverWorkspaceContents(agentDir);
        if (!string.IsNullOrEmpty(listing))
        {
            sb.AppendLine(listing);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Discover directories and .md files in the workspace that are NOT bootstrap files.
    /// Returns a formatted listing the LLM can use to decide what to read.
    /// </summary>
    private static string DiscoverWorkspaceContents(string agentDir)
    {
        var sb = new StringBuilder();
        var hasContent = false;

        try
        {
            // List subdirectories that contain .md files
            foreach (var dir in Directory.GetDirectories(agentDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".")) continue; // skip hidden dirs
                if (!AllowedDirectories.Contains(dirName)) continue;

                var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("."))
                    .Where(f => IsRecentEnough(f, dirName))
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mdFiles.Count == 0) continue;

                if (!hasContent)
                {
                    sb.AppendLine("## Workspace Contents");
                    sb.AppendLine();
                    sb.AppendLine("The following directories and files exist in your workspace.");
                    sb.AppendLine("Files listed above are already loaded. For everything below, use the `read` tool to access them.");
                    sb.AppendLine();
                    hasContent = true;
                }

                sb.AppendLine($"### {dirName}/ ({mdFiles.Count} files)");
                sb.AppendLine(string.Join(", ", mdFiles!));
                sb.AppendLine();
            }
        }
        catch (Exception)
        {
            // Silently skip if directory is not accessible
        }

        return hasContent ? sb.ToString() : string.Empty;
    }

    private static List<(string Path, string Content)> DiscoverBootstrapFiles(string agentDir)
    {
        var found = new List<(string Path, string Content)>();

        foreach (var fileName in BootstrapFiles)
        {
            var fullPath = Path.Combine(agentDir, fileName);
            if (!File.Exists(fullPath)) continue;

            var content = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(content)) continue;

            found.Add((fileName, content));
        }

        // Sort by priority order
        found.Sort((a, b) =>
        {
            var orderA = BootstrapFileOrder.GetValueOrDefault(a.Path, 99);
            var orderB = BootstrapFileOrder.GetValueOrDefault(b.Path, 99);
            return orderA.CompareTo(orderB);
        });

        return found;
    }

    public string AssembleDynamicContext(
        string? workingState = null,
        string? recentMemory = null,
        string? groupContext = null)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(workingState))
        {
            sb.AppendLine("## Working State");
            sb.AppendLine(workingState);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(recentMemory))
        {
            sb.AppendLine("## Recent Memory");
            sb.AppendLine(recentMemory);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(groupContext))
        {
            sb.AppendLine("## Group Context");
            sb.AppendLine(groupContext);
            sb.AppendLine();
        }

        var result = sb.ToString();
        if (_dynamicTokenBudget > 0 && EstimateTokens(result) > _dynamicTokenBudget)
            result = TruncateToTokenBudget(result, _dynamicTokenBudget);

        return result;
    }

    /// <summary>
    /// For memory/ files, only include those from the last N days (by filename date prefix).
    /// Other directories: always include.
    /// </summary>
    private static bool IsRecentEnough(string filePath, string dirName)
    {
        if (!string.Equals(dirName, "memory", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(filePath);
        // Memory files are prefixed with YYYY-MM-DD
        if (fileName.Length >= 10 && fileName[4] == '-' && fileName[7] == '-')
        {
            var datePrefix = fileName[..10];
            if (DateTime.TryParseExact(datePrefix, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                return fileDate >= DateTime.Today.AddDays(-MemoryLookbackDays);
            }
        }

        // Can't parse date — include it (conservative)
        return true;
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }

    private static string TruncateToTokenBudget(string text, int tokenBudget)
    {
        var charBudget = tokenBudget * 4;
        if (text.Length <= charBudget) return text;

        var cutoff = charBudget;
        var lastNewline = text.LastIndexOf('\n', cutoff);
        if (lastNewline > 0)
            cutoff = lastNewline;

        return text[..cutoff] + "\n\n[Content truncated to fit token budget]";
    }
}
