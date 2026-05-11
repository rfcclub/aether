using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aether.Agent;

public class ToolExecutor
{
    protected ToolExecutor() { _options = new SandboxOptions("none", 30000, 512, false, Array.Empty<string>()); _sandboxDisabled = true; _allowedPaths = Array.Empty<string>(); _deniedPaths = Array.Empty<string>(); }

    private readonly SandboxOptions _options;
    private string[] _allowedPaths;
    private string[] _deniedPaths;
    private readonly bool _sandboxDisabled;
    private readonly ILogger<ToolExecutor>? _logger;

    public ToolExecutor(IConfiguration configuration, ILogger<ToolExecutor>? logger = null)
        : this(SandboxOptions.FromConfiguration(configuration), null, null, logger)
    {
    }

    public ToolExecutor(SandboxOptions options)
        : this(options, null, null, null)
    {
    }

    public ToolExecutor(SandboxOptions options, string? workspacePath, SpecToolsSection? toolsConfig = null, ILogger<ToolExecutor>? logger = null)
    {
        _options = options;
        _logger = logger;
        _sandboxDisabled = string.Equals(options.Type, "none", StringComparison.OrdinalIgnoreCase);
        _allowedPaths = Array.Empty<string>();
        _deniedPaths = Array.Empty<string>();
        _logger?.LogInformation("ToolExecutor init: type={Type} sandboxDisabled={SandboxDisabled} workspace={Workspace}", options.Type, _sandboxDisabled, workspacePath ?? "(null)");
        RebuildPaths(workspacePath, toolsConfig);
    }

    public void SetAgentContext(string workspace, SpecToolsSection? toolsConfig = null)
    {
        _logger?.LogInformation("ToolExecutor SetAgentContext: workspace={Workspace} sandboxDisabled={SandboxDisabled}", workspace, _sandboxDisabled);
        RebuildPaths(workspace, toolsConfig);
    }

    private void RebuildPaths(string? workspacePath, SpecToolsSection? toolsConfig)
    {
        var paths = new List<string>();

        // Workspace is always allowed
        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            var normalized = Path.GetFullPath(workspacePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            paths.Add(normalized);
        }

        // Explicit allowed paths from SandboxOptions
        foreach (var path in _options.AllowedPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            paths.Add(Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        // Extra allowed paths from per-agent tools config
        if (toolsConfig?.File?.AllowedPaths is { Count: > 0 } extraPaths)
        {
            foreach (var path in extraPaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                paths.Add(Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        _allowedPaths = paths.Distinct().ToArray();

        // Build denied paths from per-agent config
        var denied = new List<string>();
        if (toolsConfig?.File?.DeniedPaths is { Count: > 0 } deniedPathsList)
        {
            foreach (var path in deniedPathsList)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var fullDenied = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(workspacePath ?? ".", path));
                denied.Add(fullDenied.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        _deniedPaths = denied.ToArray();
    }

    public virtual ToolResult Execute(ToolCall call)
    {
        return call.Name.ToLowerInvariant() switch
        {
            "read" => Read(call),
            "glob" => Glob(call),
            "grep" => Grep(call),
            "bash" => Bash(call),
            "write" => Write(call),
            "edit" => Edit(call),
            _ => new ToolResult(false, "", $"Unknown tool: {call.Name}")
        };
    }

    public virtual Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
        => Task.FromResult(Execute(call));

    private ToolResult Read(ToolCall call)
    {
        var path = Required(call, "path");
        if (!IsPathAllowed(path))
            return new ToolResult(false, "", "Path not permitted");

        if (!File.Exists(path))
            return new ToolResult(false, "", "File not found");

        return new ToolResult(true, File.ReadAllText(path));
    }

    private ToolResult Glob(ToolCall call)
    {
        var root = call.Arguments.TryGetValue("root", out var configuredRoot) ? configuredRoot : FirstAllowedPath();
        var pattern = Required(call, "pattern");

        root = root.Replace("**", "").Replace("//", "/");
        if (!root.EndsWith('/') && !root.EndsWith('\\'))
            root += Path.DirectorySeparatorChar;

        if (!IsPathAllowed(root))
            return new ToolResult(false, "", "Path not permitted");

        if (!Directory.Exists(root))
            return new ToolResult(false, "", "Directory not found");

        try
        {
            var files = Directory
                .EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return new ToolResult(true, string.Join(Environment.NewLine, files));
        }
        catch (DirectoryNotFoundException)
        {
            return new ToolResult(false, "", "Directory not found during enumeration");
        }
    }

    private ToolResult Grep(ToolCall call)
    {
        var path = Required(call, "path");
        var pattern = Required(call, "pattern");
        if (!IsPathAllowed(path))
            return new ToolResult(false, "", "Path not permitted");

        var contextLines = 0;
        if (call.Arguments.TryGetValue("context_lines", out var contextValue)
            && (!int.TryParse(contextValue, out contextLines) || contextLines < 0))
        {
            return new ToolResult(false, "", "context_lines must be a non-negative integer");
        }

        var regex = new Regex(pattern, RegexOptions.Compiled);
        var files = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            : File.Exists(path)
                ? new[] { path }
                : Array.Empty<string>();
        var output = new StringBuilder();

        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(file);
            var emitted = new HashSet<int>();

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (regex.IsMatch(line))
                {
                    var first = Math.Max(0, index - contextLines);
                    var last = Math.Min(lines.Length - 1, index + contextLines);
                    for (var contextIndex = first; contextIndex <= last; contextIndex++)
                    {
                        if (!emitted.Add(contextIndex))
                            continue;

                        var separator = contextIndex == index ? ':' : '-';
                        output
                            .Append(file)
                            .Append(':')
                            .Append(contextIndex + 1)
                            .Append(separator)
                            .AppendLine(lines[contextIndex]);
                    }
                }
            }
        }

        return new ToolResult(true, output.ToString().TrimEnd());
    }

    private ToolResult Bash(ToolCall call)
    {
        var command = Required(call, "command");
        var cwd = call.Arguments.TryGetValue("cwd", out var configuredCwd) ? configuredCwd : FirstAllowedPath();
        if (!IsPathAllowed(cwd))
            return new ToolResult(false, "", "Path not permitted");

        if (!Directory.Exists(cwd))
            return new ToolResult(false, "", "Directory not found");

        var startInfo = CreateBashStartInfo(command, cwd);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
                return new ToolResult(false, "", "Command failed to start");

            process.WaitForExit(Math.Max(1, _options.TimeoutMs));

            if (!process.HasExited)
            {
                TryKillProcessTree(process);
                return new ToolResult(false, "", "Command timed out");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            var output = Truncate(FormatCommandOutput(stdout, stderr), _options.MaxOutputBytes);

            if (process.ExitCode == 0)
                return new ToolResult(true, output);

            return new ToolResult(false, output, $"Command exited with code {process.ExitCode}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return new ToolResult(false, "", $"Command failed: {ex.Message}");
        }
    }

    private ToolResult Write(ToolCall call)
    {
        var path = Required(call, "path");
        var content = Required(call, "content");
        if (!IsPathAllowed(path))
            return new ToolResult(false, "", "Path not permitted");

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            if (!IsPathAllowed(parent))
                return new ToolResult(false, "", "Path not permitted");

            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, content);
        return new ToolResult(true, $"Wrote {path}");
    }

    private ToolResult Edit(ToolCall call)
    {
        var path = Required(call, "path");
        var oldText = Required(call, "old");
        var newText = Required(call, "new");
        if (!IsPathAllowed(path))
            return new ToolResult(false, "", "Path not permitted");

        if (!File.Exists(path))
            return new ToolResult(false, "", "File not found");

        var content = File.ReadAllText(path);
        if (!content.Contains(oldText, StringComparison.Ordinal))
            return new ToolResult(false, "", "Text not found");

        var updated = content.Replace(oldText, newText, StringComparison.Ordinal);
        File.WriteAllText(path, updated);
        return new ToolResult(true, $"Edited {path}");
    }

    private bool IsPathAllowed(string path)
    {
        // Sandbox type "none" disables all path restrictions
        if (_sandboxDisabled) return true;

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Check denied paths first — they take precedence over allowed paths
        foreach (var denied in _deniedPaths)
        {
            if (string.Equals(fullPath, denied, StringComparison.Ordinal)
                || fullPath.StartsWith(denied + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || fullPath.StartsWith(denied + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (_allowedPaths.Length == 0)
            return true;

        foreach (var allowed in _allowedPaths)
        {
            if (string.Equals(fullPath, allowed, StringComparison.Ordinal)
                || fullPath.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || fullPath.StartsWith(allowed + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }
        }

        _logger?.LogWarning("ToolExecutor IsPathAllowed: DENIED path='{Path}' sandboxDisabled={SandboxDisabled} allowedPaths=[{Allowed}] deniedPaths=[{Denied}]",
            path, _sandboxDisabled, string.Join(", ", _allowedPaths), string.Join(", ", _deniedPaths));

        return false;
    }

    private string FirstAllowedPath()
    {
        return _allowedPaths.FirstOrDefault(Directory.Exists) ?? Directory.GetCurrentDirectory();
    }

    private static ProcessStartInfo CreateBashStartInfo(string command, string cwd)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
            .WithArgument("/c")
            .WithArgument(command);
        }

        return new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
        .WithArgument("-lc")
        .WithArgument(command);
    }

    private static string FormatCommandOutput(string stdout, string stderr)
    {
        var output = new StringBuilder();
        if (!string.IsNullOrEmpty(stdout))
        {
            output.AppendLine("[stdout]");
            output.Append(stdout);
            if (!stdout.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                output.AppendLine();
            }
        }

        if (!string.IsNullOrEmpty(stderr))
        {
            output.AppendLine("[stderr]");
            output.Append(stderr);
            if (!stderr.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                output.AppendLine();
            }
        }

        return output.ToString().TrimEnd();
    }

    private static string Truncate(string value, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            return "[truncated]";
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return value;
        }

        return Encoding.UTF8.GetString(bytes, 0, maxBytes) + Environment.NewLine + "[truncated]";
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string Required(ToolCall call, string name)
    {
        if (call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        throw new ArgumentException($"Missing required argument: {name}");
    }
}

internal static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo WithArgument(this ProcessStartInfo startInfo, string argument)
    {
        startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}

public sealed record SandboxOptions(
    string Type,
    int TimeoutMs,
    int MaxMemoryMb,
    bool NetworkEnabled,
    IReadOnlyList<string> AllowedPaths,
    int MaxOutputBytes = 65536)
{
    private const int DefaultTimeoutMs = 30000;
    private const int DefaultMaxMemoryMb = 512;
    private const int DefaultMaxOutputBytes = 65536;

    public static SandboxOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("sandbox");
        return new SandboxOptions(
            Type: section["type"] ?? "process",
            TimeoutMs: section.GetValue("timeout_ms", DefaultTimeoutMs),
            MaxMemoryMb: section.GetValue("max_memory_mb", DefaultMaxMemoryMb),
            NetworkEnabled: section.GetValue("network_enabled", false),
            AllowedPaths: section.GetSection("allowed_paths").Get<string[]>() ?? Array.Empty<string>(),
            MaxOutputBytes: section.GetValue("max_output_bytes", DefaultMaxOutputBytes));
    }
}
