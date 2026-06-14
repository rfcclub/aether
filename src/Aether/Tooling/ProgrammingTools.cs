using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public sealed class MkdirTool : FileToolBase
{
    public override string Name => "mkdir";
    public override string Description => "Create directories safely without spawning shell";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "Directory path relative to workspace" }
            },
            "required": ["path"],
            "additionalProperties": false
        }
        """).RootElement;

    public MkdirTool(ILogger<MkdirTool> logger) : base(logger) { }

    public override Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("Write operations are not allowed in this session.");

        var path = args.GetProperty("path").GetString()!;
        var resolved = ResolvePath(path, sandbox);

        if (!Directory.Exists(resolved))
            Directory.CreateDirectory(resolved);

        return Task.FromResult<object>("Directory created successfully.");
    }
}

public sealed class DeleteFileTool : FileToolBase
{
    public override string Name => "delete_file";
    public override string Description => "Delete a file safely by moving it to the trash folder";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path relative to workspace to delete" }
            },
            "required": ["path"],
            "additionalProperties": false
        }
        """).RootElement;

    public DeleteFileTool(ILogger<DeleteFileTool> logger) : base(logger) { }

    public override Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("Write operations are not allowed in this session.");

        var path = args.GetProperty("path").GetString()!;
        var resolved = ResolvePath(path, sandbox);

        if (!File.Exists(resolved))
            throw new FileNotFoundException($"File not found: {path}");

        // Move to trash
        var trashDir = Path.Combine(sandbox.WorkspacePath, ".aether", "trash");
        if (!Directory.Exists(trashDir))
            Directory.CreateDirectory(trashDir);

        var dest = Path.Combine(trashDir, Path.GetFileName(resolved) + "_" + DateTime.UtcNow.Ticks + ".bak");
        File.Move(resolved, dest);

        return Task.FromResult<object>($"File moved to trash: {Path.GetRelativePath(sandbox.WorkspacePath, dest)}");
    }
}

public sealed class MoveFileTool : FileToolBase
{
    public override string Name => "move_file";
    public override string Description => "Rename or move a file safely";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "source": { "type": "string", "description": "Source file path relative to workspace" },
                "destination": { "type": "string", "description": "Destination file path relative to workspace" }
            },
            "required": ["source", "destination"],
            "additionalProperties": false
        }
        """).RootElement;

    public MoveFileTool(ILogger<MoveFileTool> logger) : base(logger) { }

    public override Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("Write operations are not allowed in this session.");

        var src = args.GetProperty("source").GetString()!;
        var dest = args.GetProperty("destination").GetString()!;

        var resolvedSrc = ResolvePath(src, sandbox);
        var resolvedDest = ResolvePath(dest, sandbox);

        if (!File.Exists(resolvedSrc))
            throw new FileNotFoundException($"Source file not found: {src}");

        var destDir = Path.GetDirectoryName(resolvedDest);
        if (destDir is not null && !Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        File.Move(resolvedSrc, resolvedDest, overwrite: true);

        return Task.FromResult<object>("File moved successfully.");
    }
}

public sealed class GitStatusTool : IToolImplementation
{
    public string Name => "git_status";
    public string Description => "Get structured git status showing unstaged, staged, untracked, and deleted files";
    public JsonElement ParametersSchema => JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement;

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var bash = new BashTool(Microsoft.Extensions.Logging.Abstractions.NullLogger<BashTool>.Instance);
        var res = (BashTool.BashResult)await bash.ExecuteAsync(JsonDocument.Parse("""{"command":"git status --porcelain"}""").RootElement, sandbox, ct);
        if (res.ExitCode != 0)
            return $"Git status failed: {res.Output}";

        var lines = res.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<object>();

        foreach (var line in lines)
        {
            if (line.Length < 4) continue;
            var status = line[..2];
            var path = line[3..].Trim('"');
            results.Add(new { status, path });
        }

        return JsonSerializer.Serialize(results);
    }
}

public sealed class GitDiffTool : IToolImplementation
{
    public string Name => "git_diff";
    public string Description => "Get structured git diff showing changes in the repository";
    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "file": { "type": "string", "description": "Optional file path to get diff for" },
                "staged": { "type": "boolean", "description": "Get staged changes if true; default false" }
            },
            "additionalProperties": false
        }
        """).RootElement;

    // Only allow safe path characters — no shell metacharacters
    private static readonly Regex _safePathRegex = new(@"^[A-Za-z0-9_./ -]+$", RegexOptions.Compiled);

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var file = args.TryGetProperty("file", out var fEl) && fEl.ValueKind == JsonValueKind.String
            ? fEl.GetString()!
            : "";
        var staged = args.TryGetProperty("staged", out var sEl) && sEl.ValueKind == JsonValueKind.True;

        if (!string.IsNullOrEmpty(file) && !_safePathRegex.IsMatch(file))
            throw new ArgumentException("File path contains invalid characters.");

        var cmd = "git diff";
        if (staged) cmd += " --staged";
        if (!string.IsNullOrEmpty(file)) cmd += $" -- {file}";

        var bash = new BashTool(Microsoft.Extensions.Logging.Abstractions.NullLogger<BashTool>.Instance);
        var cmdJson = JsonSerializer.SerializeToElement(new { command = cmd });
        var res = (BashTool.BashResult)await bash.ExecuteAsync(cmdJson, sandbox, ct);
        return res.Output;
    }
}

public sealed class RunCommandTool : IToolImplementation
{
    public string Name => "run_command";
    public string Description => "Run a shell command with safety rules, timeouts, and cancellation support";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "command": { "type": "string", "description": "Shell command to run" }
            },
            "required": ["command"],
            "additionalProperties": false
        }
        """).RootElement;

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var bash = new BashTool(Microsoft.Extensions.Logging.Abstractions.NullLogger<BashTool>.Instance);
        var res = await bash.ExecuteAsync(args, sandbox, ct);
        return res;
    }
}

public sealed class ApplyPatchTool : FileToolBase
{
    public override string Name => "apply_patch";
    public override string Description => "Apply git unified diff format patch to a file";

    public override JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": { "type": "string", "description": "File path relative to workspace" },
                "patch": { "type": "string", "description": "Unified diff format patch string" }
            },
            "required": ["path", "patch"],
            "additionalProperties": false
        }
        """).RootElement;

    public ApplyPatchTool(ILogger<ApplyPatchTool> logger) : base(logger) { }

    public override async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        if (!sandbox.AllowWrites)
            throw new UnauthorizedAccessException("Write operations are not allowed in this session.");

        var path = args.GetProperty("path").GetString()!;
        var patch = args.GetProperty("patch").GetString() ?? "";
        var resolved = ResolvePath(path, sandbox);

        if (!File.Exists(resolved))
            throw new FileNotFoundException($"File not found: {path}");

        // Defense-in-depth: explicit sandbox boundary check
        var workspaceRoot = Path.GetFullPath(sandbox.WorkspacePath) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(resolved).StartsWith(workspaceRoot, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Path escapes workspace: {path}");

        var content = await File.ReadAllTextAsync(resolved, ct);
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();

        var patchLines = patch.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var hToDelete = new List<string>();
        var hToAdd = new List<string>();
        var applying = false;

        foreach (var pLine in patchLines)
        {
            if (pLine.StartsWith("@@"))
            {
                applying = true;
                continue;
            }
            if (!applying) continue;

            if (pLine.StartsWith('-'))
                hToDelete.Add(pLine[1..]);
            else if (pLine.StartsWith('+'))
                hToAdd.Add(pLine[1..]);
            else if (pLine.StartsWith(' '))
            {
                hToDelete.Add(pLine[1..]);
                hToAdd.Add(pLine[1..]);
            }
        }

        var oldBlock = string.Join("\n", hToDelete).Replace("\r", "");
        var newBlock = string.Join("\n", hToAdd).Replace("\r", "");

        // If diff block is empty or matches context
        if (string.IsNullOrEmpty(oldBlock))
            return "No patch changes detected.";

        var contentNormalized = content.Replace("\r\n", "\n").Replace("\r", "");
        var oldBlockNormalized = oldBlock.Replace("\r\n", "\n").Replace("\r", "");
        var newBlockNormalized = newBlock.Replace("\r\n", "\n").Replace("\r", "");

        var idx = contentNormalized.IndexOf(oldBlockNormalized, StringComparison.Ordinal);
        if (idx < 0)
        {
            // Fallback: try direct patch command via shell if native C# parsing fails or context differs
            var tempPatchPath = Path.Combine(sandbox.WorkspacePath, $".aether_patch_{Guid.NewGuid().ToString("N")[..8]}.patch");
            await File.WriteAllTextAsync(tempPatchPath, patch, ct);
            try
            {
                var bash = new BashTool(Microsoft.Extensions.Logging.Abstractions.NullLogger<BashTool>.Instance);
                var patchCmd = $"patch {resolved} {tempPatchPath}";
                var cmdJson = JsonSerializer.SerializeToElement(new { command = patchCmd });
                var res = (BashTool.BashResult)await bash.ExecuteAsync(cmdJson, sandbox, ct);
                if (res.ExitCode == 0)
                    return "Patch applied successfully via shell.";
                else
                    throw new InvalidOperationException($"Failed to apply patch: {res.Output}");
            }
            finally
            {
                if (File.Exists(tempPatchPath)) File.Delete(tempPatchPath);
            }
        }

        var updated = contentNormalized[..idx] + newBlockNormalized + contentNormalized[(idx + oldBlockNormalized.Length)..];
        
        var tmp = resolved + ".tmp";
        await File.WriteAllTextAsync(tmp, updated.Replace("\n", Environment.NewLine), ct);
        File.Move(tmp, resolved, overwrite: true);

        return "Patch applied successfully.";
    }
}
