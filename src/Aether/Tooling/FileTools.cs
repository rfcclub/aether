using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

/// <summary>
/// Shared base for file operation tools. Provides path resolution and sandbox enforcement.
/// </summary>
public abstract class FileToolBase : IToolImplementation
{
    protected readonly ILogger _logger;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonElement ParametersSchema { get; }

    protected FileToolBase(ILogger logger) => _logger = logger;

    public abstract Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct);

    protected string ResolvePath(string path, ISandboxContext sandbox)
    {
        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(sandbox.WorkspacePath, path));

        if (!sandbox.IsPathAllowed(resolved))
            throw new UnauthorizedAccessException($"Path not permitted: {path}");

        return resolved;
    }
}

public sealed class ReadTool : FileToolBase
{
    public override string Name => "read";
    public override string Description => "Read a file from the workspace";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path relative to workspace" }
            },
            "required": ["path"]
        }
        """).RootElement;

    public ReadTool(ILogger<ReadTool> logger) : base(logger) { }

    public override async Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var resolved = ResolvePath(path, sandbox);

        if (!File.Exists(resolved))
            throw new FileNotFoundException($"File not found: {path}");

        return await File.ReadAllTextAsync(resolved, ct);
    }
}

public sealed class WriteTool : FileToolBase
{
    public override string Name => "write";
    public override string Description => "Write content to a file";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path relative to workspace" },
                "content": { "type": "string", "description": "Content to write" }
            },
            "required": ["path", "content"]
        }
        """).RootElement;

    public WriteTool(ILogger<WriteTool> logger) : base(logger) { }

    public override async Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("Write operations are not allowed in this session.");

        var path = args.GetProperty("path").GetString()!;
        var content = args.GetProperty("content").GetString() ?? "";
        var resolved = ResolvePath(path, sandbox);

        var dir = Path.GetDirectoryName(resolved);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Atomic write: temp file then rename
        var tmp = resolved + ".tmp";
        await File.WriteAllTextAsync(tmp, content, ct);
        File.Move(tmp, resolved, overwrite: true);

        return "Written.";
    }
}

public sealed class EditTool : FileToolBase
{
    public override string Name => "edit";
    public override string Description => "Replace text in a file";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path relative to workspace" },
                "old_string": { "type": "string", "description": "Text to replace" },
                "new_string": { "type": "string", "description": "Replacement text" }
            },
            "required": ["path", "old_string", "new_string"]
        }
        """).RootElement;

    public EditTool(ILogger<EditTool> logger) : base(logger) { }

    public override async Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("Write operations are not allowed in this session.");

        var path = args.GetProperty("path").GetString()!;
        var oldStr = args.GetProperty("old_string").GetString() ?? "";
        var newStr = args.GetProperty("new_string").GetString() ?? "";
        var resolved = ResolvePath(path, sandbox);

        if (!File.Exists(resolved))
            throw new FileNotFoundException($"File not found: {path}");

        var content = await File.ReadAllTextAsync(resolved, ct);
        var idx = content.IndexOf(oldStr, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException($"Text not found in file: '{oldStr}'");

        var updated = content[..idx] + newStr + content[(idx + oldStr.Length)..];

        var tmp = resolved + ".tmp";
        await File.WriteAllTextAsync(tmp, updated, ct);
        File.Move(tmp, resolved, overwrite: true);

        return "Edited.";
    }
}

public sealed class GlobTool : FileToolBase
{
    public override string Name => "glob";
    public override string Description => "Find files matching a pattern";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "pattern": { "type": "string", "description": "Glob pattern (e.g., *.cs, **/*.md)" }
            },
            "required": ["pattern"]
        }
        """).RootElement;

    public GlobTool(ILogger<GlobTool> logger) : base(logger) { }

    public override Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct)
    {
        var pattern = args.GetProperty("pattern").GetString()!;

        var matches = System.IO.Directory.GetFiles(
            sandbox.WorkspacePath,
            pattern,
            SearchOption.AllDirectories);

        var relative = matches
            .Select(m => Path.GetRelativePath(sandbox.WorkspacePath, m))
            .OrderBy(p => p)
            .ToList();

        return Task.FromResult<object>(string.Join("\n", relative));
    }
}

public sealed class GrepTool : FileToolBase
{
    public override string Name => "grep";
    public override string Description => "Search for text in files";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "pattern": { "type": "string", "description": "Text or regex to search for" },
                "path": { "type": "string", "description": "Directory or file to search in (default: workspace root)" }
            },
            "required": ["pattern"]
        }
        """).RootElement;

    public GrepTool(ILogger<GrepTool> logger) : base(logger) { }

    public override async Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct)
    {
        var pattern = args.GetProperty("pattern").GetString()!;
        var searchPath = args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : ".";

        var resolved = ResolvePath(searchPath, sandbox);
        var results = new List<string>();

        var files = File.Exists(resolved)
            ? new[] { resolved }
            : Directory.GetFiles(resolved, "*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            if (!sandbox.IsPathAllowed(file)) continue;

            try
            {
                var lines = await File.ReadAllLinesAsync(file, ct);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var relPath = Path.GetRelativePath(sandbox.WorkspacePath, file);
                        results.Add($"{relPath}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Skip unreadable files
            }
        }

        results = results.Take(200).ToList(); // cap results
        return string.Join("\n", results);
    }
}
