using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

internal static class ToolJsonSchemas
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

public sealed class SkillListTool : IToolImplementation
{
    private readonly ILogger<SkillListTool> _logger;

    public string Name => "skill_list";
    public string Description => "List available skills in the active workspace.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");

    public SkillListTool(ILogger<SkillListTool> logger) => _logger = logger;

    public Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var skillsDir = Path.Combine(sandbox.WorkspacePath, "skills");
        if (!Directory.Exists(skillsDir))
            return Task.FromResult<object>("No skills directory found.");

        var skills = Directory.EnumerateDirectories(skillsDir)
            .Where(dir => File.Exists(Path.Combine(dir, "SKILL.md")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<object>(skills.Count == 0 ? "No skills found." : string.Join("\n", skills));
    }
}

public sealed class SkillReadTool : IToolImplementation
{
    public string Name => "skill_read";
    public string Description => "Read one workspace skill's SKILL.md by skill name.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Skill directory name" }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """);

    public Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var name = args.GetProperty("name").GetString() ?? "";
        if (!IsSafeName(name))
            throw new UnauthorizedAccessException("skill_read: invalid skill name.");

        var path = Path.Combine(sandbox.WorkspacePath, "skills", name, "SKILL.md");
        if (!sandbox.IsPathAllowed(path))
            throw new UnauthorizedAccessException("skill_read: path not permitted.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"skill_read: skill not found: {name}");

        return Task.FromResult<object>(File.ReadAllText(path));
    }

    private static bool IsSafeName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !value.Contains("..", StringComparison.Ordinal)
        && !value.Contains('/', StringComparison.Ordinal)
        && !value.Contains('\\', StringComparison.Ordinal);
}

public sealed class MemoryReadTool : IToolImplementation
{
    public string Name => "memory_read";
    public string Description => "Read a file under the workspace memory directory.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Memory file path relative to memory/; defaults to today's YYYY-MM-DD.md" }
          },
          "additionalProperties": false
        }
        """);

    public Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var relative = args.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String
            ? pathEl.GetString()!
            : $"{DateTime.UtcNow:yyyy-MM-dd}.md";

        var path = ResolveMemoryPath(sandbox, relative);
        if (!File.Exists(path))
            throw new FileNotFoundException($"memory_read: file not found: {relative}");

        return Task.FromResult<object>(File.ReadAllText(path));
    }

    internal static string ResolveMemoryPath(SandboxContext sandbox, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("memory path is required");
        if (Path.IsPathRooted(relativePath) || relativePath.Contains("..", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("memory path must be relative to memory/.");

        var memoryRoot = Path.GetFullPath(Path.Combine(sandbox.WorkspacePath, "memory"));
        var resolved = Path.GetFullPath(Path.Combine(memoryRoot, relativePath));
        if (!resolved.StartsWith(memoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolved, memoryRoot, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("memory path escaped memory/.");
        if (!sandbox.IsPathAllowed(resolved))
            throw new UnauthorizedAccessException("memory path not permitted.");
        return resolved;
    }
}

public sealed class MemoryWriteTool : IToolImplementation
{
    public string Name => "memory_write";
    public string Description => "Append or write a memory note under workspace memory/. Defaults to today's daily memory file.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""
        {
          "type": "object",
          "properties": {
            "content": { "type": "string" },
            "path": { "type": "string", "description": "Memory file path relative to memory/; defaults to today's YYYY-MM-DD.md" },
            "append": { "type": "boolean", "description": "Append instead of overwrite; default true" }
          },
          "required": ["content"],
          "additionalProperties": false
        }
        """);

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("memory_write: writes are not allowed in this session.");

        var content = args.GetProperty("content").GetString() ?? "";
        var relative = args.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String
            ? pathEl.GetString()!
            : $"{DateTime.UtcNow:yyyy-MM-dd}.md";
        var append = !args.TryGetProperty("append", out var appendEl)
                     || appendEl.ValueKind != JsonValueKind.False;

        var path = MemoryReadTool.ResolveMemoryPath(sandbox, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (append)
        {
            var text = content.EndsWith('\n') ? content : content + Environment.NewLine;
            await File.AppendAllTextAsync(path, text, ct);
            return $"Appended memory/{relative}.";
        }

        await File.WriteAllTextAsync(path, content, ct);
        return $"Wrote memory/{relative}.";
    }
}

public sealed class MemorySearchTool : IToolImplementation
{
    public string Name => "memory_search";
    public string Description => "Search Markdown memory files in workspace memory/.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "limit": { "type": "integer" }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """);

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString() ?? "";
        var limit = args.TryGetProperty("limit", out var limitEl) && limitEl.ValueKind == JsonValueKind.Number
            ? Math.Clamp(limitEl.GetInt32(), 1, 100)
            : 25;
        var memoryRoot = Path.Combine(sandbox.WorkspacePath, "memory");
        if (!Directory.Exists(memoryRoot))
            return "No memory directory found.";

        var results = new List<string>();
        foreach (var file in Directory.EnumerateFiles(memoryRoot, "*.md", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!sandbox.IsPathAllowed(file)) continue;
            var lines = await File.ReadAllLinesAsync(file, ct);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(sandbox.WorkspacePath, file);
                    results.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
                    if (results.Count >= limit)
                        return string.Join("\n", results);
                }
            }
        }

        return results.Count == 0 ? "No memory matches found." : string.Join("\n", results);
    }
}

public sealed class SessionStatusTool : IToolImplementation
{
    public string Name => "session_status";
    public string Description => "Report basic active workspace/session tool context.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");

    public Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Workspace: {sandbox.WorkspacePath}");
        sb.AppendLine($"Writes: {(sandbox.AllowWrites ? "enabled" : "disabled")}");
        sb.AppendLine($"Allowed commands: {(sandbox.AllowedCommands.Count == 0 ? "all" : string.Join(", ", sandbox.AllowedCommands))}");
        return Task.FromResult<object>(sb.ToString().TrimEnd());
    }
}

public sealed class SessionResetTool : IToolImplementation
{
    public string Name => "session_reset";
    public string Description => "Request a working-context reset after this tool call.";
    public JsonElement ParametersSchema => ToolJsonSchemas.Parse("""{"type":"object","properties":{},"additionalProperties":false}""");

    public Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct) =>
        Task.FromResult<object>("Session reset requested.");
}
