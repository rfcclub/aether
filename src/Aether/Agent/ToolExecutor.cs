using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Aether.Agent;

public sealed class ToolExecutor : IToolExecutor
{
    private readonly string[] _allowedPaths;

    public ToolExecutor(IConfiguration configuration)
        : this(SandboxOptions.FromConfiguration(configuration))
    {
    }

    public ToolExecutor(SandboxOptions options)
    {
        _allowedPaths = options.AllowedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();
    }

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        return call.Name.ToLowerInvariant() switch
        {
            "read" => ReadAsync(call, ct),
            "glob" => GlobAsync(call, ct),
            "grep" => GrepAsync(call, ct),
            "bash" or "write" or "edit" => Task.FromResult(new ToolResult(false, "", $"Tool not enabled yet: {call.Name}")),
            _ => Task.FromResult(new ToolResult(false, "", $"Unknown tool: {call.Name}"))
        };
    }

    private async Task<ToolResult> ReadAsync(ToolCall call, CancellationToken ct)
    {
        var path = Required(call, "path");
        if (!IsPathAllowed(path))
        {
            return new ToolResult(false, "", "Path not permitted");
        }

        if (!File.Exists(path))
        {
            return new ToolResult(false, "", "File not found");
        }

        return new ToolResult(true, await File.ReadAllTextAsync(path, ct));
    }

    private Task<ToolResult> GlobAsync(ToolCall call, CancellationToken ct)
    {
        var root = call.Arguments.TryGetValue("root", out var configuredRoot) ? configuredRoot : FirstAllowedPath();
        var pattern = Required(call, "pattern");
        if (!IsPathAllowed(root))
        {
            return Task.FromResult(new ToolResult(false, "", "Path not permitted"));
        }

        if (!Directory.Exists(root))
        {
            return Task.FromResult(new ToolResult(false, "", "Directory not found"));
        }

        var files = Directory
            .EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(new ToolResult(true, string.Join(Environment.NewLine, files)));
    }

    private async Task<ToolResult> GrepAsync(ToolCall call, CancellationToken ct)
    {
        var path = Required(call, "path");
        var pattern = Required(call, "pattern");
        if (!IsPathAllowed(path))
        {
            return new ToolResult(false, "", "Path not permitted");
        }

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
            ct.ThrowIfCancellationRequested();
            var lines = await File.ReadAllLinesAsync(file, ct);
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
                        {
                            continue;
                        }

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

    private bool IsPathAllowed(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return _allowedPaths.Any(allowed =>
            string.Equals(fullPath, allowed, StringComparison.Ordinal)
            || fullPath.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || fullPath.StartsWith(allowed + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
    }

    private string FirstAllowedPath()
    {
        return _allowedPaths.FirstOrDefault(Directory.Exists) ?? Directory.GetCurrentDirectory();
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
