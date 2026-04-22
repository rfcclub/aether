# Aether NanoClaw Basic First Five Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Aether from PicoClaw-useful foundation toward NanoClaw-basic by preserving the current baseline, enabling safe command execution, adding mutating file tools, and extending provider contracts for tool calls.

**Architecture:** Keep the first milestone inside the existing Track B boundary: `Agent/` owns tool execution, `Providers/` owns LLM request/response shape, and `tests/Aether.Tests/Program.cs` remains the dependency-light smoke harness. `AetherSoul` tool-loop integration is deliberately left for the next plan so these five steps finish as a stable checkpoint.

**Tech Stack:** .NET 9, C#, SQLite, `HttpClient`, OpenRouter chat completions, `System.Diagnostics.Process`, optional Linux `bwrap`, dependency-free console smoke tests.

---

## Current State

Aether currently has:

- `ToolExecutor` wired into DI.
- Safe managed tools: `read`, `glob`, `grep`.
- Disabled future tools: `bash`, `write`, `edit`.
- Provider contracts with plain text messages only:
  - `LlmRequest(IReadOnlyList<LlmMessage> Messages)`
  - `LlmMessage(string Role, string Content)`
  - `LlmResponse(string Content)`
- Smoke tests in `tests/Aether.Tests/Program.cs`.
- Verified build/test baseline using Windows .NET SDK from WSL:
  - `'/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln`
  - `'/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj`

## Files

- Modify: `src/Aether/Agent/IToolExecutor.cs`
  - Add optional metadata to `ToolResult` only if needed for `bash` exit code. Prefer keeping the public shape stable unless tests prove exit code must be structured.

- Modify: `src/Aether/Agent/ToolExecutor.cs`
  - Add `bash`, `write`, and `edit`.
  - Keep path safety centralized.
  - Add output truncation helper.
  - Add process execution helper.
  - Optionally add bwrap command wrapping for Linux when `SandboxOptions.Type == "bwrap"`.

- Modify: `src/Aether/Providers/ILLMProvider.cs`
  - Extend request and response contracts to carry tool definitions, tool calls, and tool results.

- Modify: `src/Aether/Providers/OpenRouterProvider.cs`
  - Serialize tool definitions.
  - Serialize normal messages and tool result messages.
  - Parse assistant content and tool calls.

- Modify: `tests/Aether.Tests/Program.cs`
  - Add smoke coverage for `bash`, `write`, `edit`, provider request shape, and provider tool-call parsing.
  - Keep tests deterministic and network-free.

- Modify: `PROGRESS.md`
  - After each completed task, update completion and verification notes.

## Task 1: Commit Current Baseline

**Files:**
- Stage existing project files and this plan.
- No code edits.

- [ ] **Step 1: Confirm no remote and no commits**

Run:

```bash
rtk git status --short --branch
rtk git remote -v
```

Expected:

```text
## No commits yet on master
?? ...
```

`git remote -v` should print nothing unless Thoor has already added a remote.

- [ ] **Step 2: Verify baseline still passes**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
Aether Track B foundation smoke tests passed.
```

- [ ] **Step 3: Commit the baseline**

Run:

```bash
rtk git add .
rtk git commit -m "chore: commit aether baseline"
```

Expected:

```text
[master <hash>] chore: commit aether baseline
```

- [ ] **Step 4: Confirm clean working tree**

Run:

```bash
rtk git status --short --branch
```

Expected:

```text
## master
```

If files remain untracked, inspect them before staging. Do not add API keys, local databases, build output, or personal runtime state.

## Task 2: Implement Sandboxed `bash`

**Files:**
- Modify: `src/Aether/Agent/ToolExecutor.cs`
- Modify: `tests/Aether.Tests/Program.cs`
- Modify: `PROGRESS.md`

**Behavior:**

`bash` accepts:

- `command` required.
- `cwd` optional, defaults to first allowed directory.

`bash` returns:

- `Succeeded = true` when exit code is `0`.
- `Succeeded = false` when exit code is non-zero, timed out, denied, or failed to start.
- `Output` contains stdout and stderr with labels.
- `Error` contains a short failure summary for non-success.

Output is truncated to `SandboxOptions.MaxOutputBytes`.

- [ ] **Step 1: Add failing `bash` smoke tests**

In `tests/Aether.Tests/Program.cs`, inside `VerifyToolExecutorAsync`, replace the disabled-tool loop so only `write` and `edit` remain disabled, then add these checks before the unknown-tool test:

```csharp
var bash = await executor.ExecuteAsync(
    new ToolCall("bash", new Dictionary<string, string>
    {
        ["cwd"] = sandboxRoot,
        ["command"] = "printf 'hello from bash'"
    }),
    CancellationToken.None);
Require(bash.Succeeded, "ToolExecutor bash must succeed for allowed cwd.");
Require(bash.Output.Contains("hello from bash", StringComparison.Ordinal), "ToolExecutor bash must return stdout.");

var bashDenied = await executor.ExecuteAsync(
    new ToolCall("bash", new Dictionary<string, string>
    {
        ["cwd"] = outsideRoot,
        ["command"] = "pwd"
    }),
    CancellationToken.None);
Require(!bashDenied.Succeeded, "ToolExecutor bash must reject cwd outside allowed paths.");
Require(bashDenied.Error == "Path not permitted", "ToolExecutor bash denied cwd must use the expected error.");

var bashExit = await executor.ExecuteAsync(
    new ToolCall("bash", new Dictionary<string, string>
    {
        ["cwd"] = sandboxRoot,
        ["command"] = "printf 'bad news' >&2; exit 7"
    }),
    CancellationToken.None);
Require(!bashExit.Succeeded, "ToolExecutor bash must fail on non-zero exit codes.");
Require(bashExit.Error == "Command exited with code 7", "ToolExecutor bash must report non-zero exit code.");
Require(bashExit.Output.Contains("bad news", StringComparison.Ordinal), "ToolExecutor bash must include stderr in output.");

var shortTimeoutExecutor = new ToolExecutor(new SandboxOptions(
    Type: "process",
    TimeoutMs: 100,
    MaxMemoryMb: 128,
    NetworkEnabled: false,
    AllowedPaths: new[] { sandboxRoot },
    MaxOutputBytes: 65536));
var bashTimeout = await shortTimeoutExecutor.ExecuteAsync(
    new ToolCall("bash", new Dictionary<string, string>
    {
        ["cwd"] = sandboxRoot,
        ["command"] = "sleep 2"
    }),
    CancellationToken.None);
Require(!bashTimeout.Succeeded, "ToolExecutor bash must fail when a command times out.");
Require(bashTimeout.Error == "Command timed out", "ToolExecutor bash timeout must use the expected error.");

var truncatingExecutor = new ToolExecutor(new SandboxOptions(
    Type: "process",
    TimeoutMs: 1000,
    MaxMemoryMb: 128,
    NetworkEnabled: false,
    AllowedPaths: new[] { sandboxRoot },
    MaxOutputBytes: 12));
var bashTruncated = await truncatingExecutor.ExecuteAsync(
    new ToolCall("bash", new Dictionary<string, string>
    {
        ["cwd"] = sandboxRoot,
        ["command"] = "printf 'abcdefghijklmnopqrstuvwxyz'"
    }),
    CancellationToken.None);
Require(bashTruncated.Succeeded, "ToolExecutor bash must succeed even when output is truncated.");
Require(bashTruncated.Output.Contains("[truncated]", StringComparison.Ordinal), "ToolExecutor bash must mark truncated output.");
```

Change the disabled loop to:

```csharp
foreach (var disabledTool in new[] { "write", "edit" })
{
    var disabled = await executor.ExecuteAsync(
        new ToolCall(disabledTool, new Dictionary<string, string>()),
        CancellationToken.None);
    Require(!disabled.Succeeded, $"ToolExecutor must keep {disabledTool} disabled until the mutating-tool slice.");
    Require(disabled.Error == $"Tool not enabled yet: {disabledTool}", $"ToolExecutor must explain that {disabledTool} is not enabled yet.");
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
Unhandled exception. System.InvalidOperationException: ToolExecutor bash must succeed for allowed cwd.
```

- [ ] **Step 3: Implement process-backed `bash`**

In `src/Aether/Agent/ToolExecutor.cs`, add:

```csharp
using System.Diagnostics;
```

Add a private field:

```csharp
private readonly SandboxOptions _options;
```

Set it in the constructor:

```csharp
public ToolExecutor(SandboxOptions options)
{
    _options = options;
    _allowedPaths = options.AllowedPaths
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(path))
        .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        .ToArray();
}
```

Update dispatch:

```csharp
return call.Name.ToLowerInvariant() switch
{
    "read" => ReadAsync(call, ct),
    "glob" => GlobAsync(call, ct),
    "grep" => GrepAsync(call, ct),
    "bash" => BashAsync(call, ct),
    "write" or "edit" => Task.FromResult(new ToolResult(false, "", $"Tool not enabled yet: {call.Name}")),
    _ => Task.FromResult(new ToolResult(false, "", $"Unknown tool: {call.Name}"))
};
```

Add:

```csharp
private async Task<ToolResult> BashAsync(ToolCall call, CancellationToken ct)
{
    var command = Required(call, "command");
    var cwd = call.Arguments.TryGetValue("cwd", out var configuredCwd) ? configuredCwd : FirstAllowedPath();
    if (!IsPathAllowed(cwd))
    {
        return new ToolResult(false, "", "Path not permitted");
    }

    if (!Directory.Exists(cwd))
    {
        return new ToolResult(false, "", "Directory not found");
    }

    var startInfo = CreateBashStartInfo(command, cwd);
    using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(Math.Max(1, _options.TimeoutMs));

    try
    {
        if (!process.Start())
        {
            return new ToolResult(false, "", "Command failed to start");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = FormatCommandOutput(stdout, stderr);
        output = Truncate(output, _options.MaxOutputBytes);

        if (process.ExitCode == 0)
        {
            return new ToolResult(true, output);
        }

        return new ToolResult(false, output, $"Command exited with code {process.ExitCode}");
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
        TryKillProcessTree(process);
        return new ToolResult(false, "", "Command timed out");
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
    {
        return new ToolResult(false, "", $"Command failed: {ex.Message}");
    }
}

private ProcessStartInfo CreateBashStartInfo(string command, string cwd)
{
    if (OperatingSystem.IsWindows())
    {
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList = { "/c", command },
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    return new ProcessStartInfo
    {
        FileName = "/bin/bash",
        ArgumentList = { "-lc", command },
        WorkingDirectory = cwd,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
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

    var truncated = Encoding.UTF8.GetString(bytes, 0, maxBytes);
    return truncated + Environment.NewLine + "[truncated]";
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
```

If tests run through Windows `dotnet.exe`, `OperatingSystem.IsWindows()` is true and the `sleep` / `printf` commands may fail. In that case, adjust the test commands using a helper:

```csharp
static string ShellEchoCommand(string text) => OperatingSystem.IsWindows()
    ? $"echo {text}"
    : $"printf '{text}'";
```

Use Windows-safe commands for timeout and large output:

```csharp
var timeoutCommand = OperatingSystem.IsWindows()
    ? "powershell -NoProfile -Command \"Start-Sleep -Seconds 2\""
    : "sleep 2";

var longOutputCommand = OperatingSystem.IsWindows()
    ? "powershell -NoProfile -Command \"Write-Output abcdefghijklmnopqrstuvwxyz\""
    : "printf 'abcdefghijklmnopqrstuvwxyz'";
```

- [ ] **Step 4: Verify `bash` tests pass**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
Build succeeded.
Aether Track B foundation smoke tests passed.
```

- [ ] **Step 5: Update progress and commit**

In `PROGRESS.md`, add a completed subsection:

```markdown
### Tool Executor Bash Slice

Status: Completed

- Added sandboxed `bash` execution through `ToolExecutor`.
- Enforced allowed working directories.
- Added timeout handling and process-tree cleanup.
- Added output capture and truncation.
- Added smoke coverage for success, denied cwd, non-zero exit, timeout, and truncation.
```

Run:

```bash
rtk git add src/Aether/Agent/ToolExecutor.cs tests/Aether.Tests/Program.cs PROGRESS.md
rtk git commit -m "feat: add sandboxed bash tool"
```

## Task 3: Add Mutating `write` And `edit`

**Files:**
- Modify: `src/Aether/Agent/ToolExecutor.cs`
- Modify: `tests/Aether.Tests/Program.cs`
- Modify: `PROGRESS.md`

**Behavior:**

`write` accepts:

- `path` required.
- `content` required.

It creates parent directories inside allowed paths and overwrites the target file.

`edit` accepts:

- `path` required.
- `old` required.
- `new` required.

It replaces exactly one or more occurrences of `old` with `new`. If `old` is absent, it fails and does not write.

- [ ] **Step 1: Add failing mutating-tool tests**

Inside `VerifyToolExecutorAsync`, after the `bash` tests and before the unknown-tool test, remove the disabled loop for `write` and `edit`, then add:

```csharp
var writePath = Path.Combine(sandboxRoot, "created", "new.txt");
var write = await executor.ExecuteAsync(
    new ToolCall("write", new Dictionary<string, string>
    {
        ["path"] = writePath,
        ["content"] = "new file contents"
    }),
    CancellationToken.None);
Require(write.Succeeded, "ToolExecutor write must create files inside allowed paths.");
Require(await File.ReadAllTextAsync(writePath) == "new file contents", "ToolExecutor write must persist content.");

var writeDenied = await executor.ExecuteAsync(
    new ToolCall("write", new Dictionary<string, string>
    {
        ["path"] = outsidePath,
        ["content"] = "leak"
    }),
    CancellationToken.None);
Require(!writeDenied.Succeeded, "ToolExecutor write must reject files outside allowed paths.");
Require(writeDenied.Error == "Path not permitted", "ToolExecutor write denied path must use the expected error.");

var edit = await executor.ExecuteAsync(
    new ToolCall("edit", new Dictionary<string, string>
    {
        ["path"] = notesPath,
        ["old"] = "beta needle",
        ["new"] = "beta replaced"
    }),
    CancellationToken.None);
Require(edit.Succeeded, "ToolExecutor edit must replace existing text.");
Require((await File.ReadAllTextAsync(notesPath)).Contains("beta replaced", StringComparison.Ordinal), "ToolExecutor edit must write replacement text.");

var editMissing = await executor.ExecuteAsync(
    new ToolCall("edit", new Dictionary<string, string>
    {
        ["path"] = notesPath,
        ["old"] = "missing text",
        ["new"] = "replacement"
    }),
    CancellationToken.None);
Require(!editMissing.Succeeded, "ToolExecutor edit must fail when old text is absent.");
Require(editMissing.Error == "Text not found", "ToolExecutor edit missing text must use the expected error.");
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
Unhandled exception. System.InvalidOperationException: ToolExecutor write must create files inside allowed paths.
```

- [ ] **Step 3: Implement `write` and `edit`**

Update dispatch in `ToolExecutor.cs`:

```csharp
"write" => WriteAsync(call, ct),
"edit" => EditAsync(call, ct),
```

Add:

```csharp
private async Task<ToolResult> WriteAsync(ToolCall call, CancellationToken ct)
{
    var path = Required(call, "path");
    var content = Required(call, "content");
    if (!IsPathAllowed(path))
    {
        return new ToolResult(false, "", "Path not permitted");
    }

    var parent = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrWhiteSpace(parent))
    {
        if (!IsPathAllowed(parent))
        {
            return new ToolResult(false, "", "Path not permitted");
        }

        Directory.CreateDirectory(parent);
    }

    await File.WriteAllTextAsync(path, content, ct);
    return new ToolResult(true, $"Wrote {path}");
}

private async Task<ToolResult> EditAsync(ToolCall call, CancellationToken ct)
{
    var path = Required(call, "path");
    var oldText = Required(call, "old");
    var newText = Required(call, "new");
    if (!IsPathAllowed(path))
    {
        return new ToolResult(false, "", "Path not permitted");
    }

    if (!File.Exists(path))
    {
        return new ToolResult(false, "", "File not found");
    }

    var content = await File.ReadAllTextAsync(path, ct);
    if (!content.Contains(oldText, StringComparison.Ordinal))
    {
        return new ToolResult(false, "", "Text not found");
    }

    var updated = content.Replace(oldText, newText, StringComparison.Ordinal);
    await File.WriteAllTextAsync(path, updated, ct);
    return new ToolResult(true, $"Edited {path}");
}
```

- [ ] **Step 4: Verify mutating tools pass**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
Build succeeded.
Aether Track B foundation smoke tests passed.
```

- [ ] **Step 5: Update progress and commit**

In `PROGRESS.md`, add:

```markdown
### Tool Executor Mutating-Tool Slice

Status: Completed

- Added allowed-path `write` support.
- Added allowed-path text replacement `edit` support.
- Added smoke coverage for file creation, rejected outside writes, successful edits, and missing-text edit failures.
```

Run:

```bash
rtk git add src/Aether/Agent/ToolExecutor.cs tests/Aether.Tests/Program.cs PROGRESS.md
rtk git commit -m "feat: add write and edit tools"
```

## Task 4: Extend Provider Tool-Call Contracts

**Files:**
- Modify: `src/Aether/Providers/ILLMProvider.cs`
- Modify: `tests/Aether.Tests/Program.cs`
- Modify: `PROGRESS.md`

**Behavior:**

Provider contracts need to represent:

- Tool definitions sent to the model.
- Assistant tool calls returned by the model.
- Tool results sent back in a later request.

Keep this provider-only. Do not change `AetherSoul` yet.

- [ ] **Step 1: Add failing contract-level tests**

In `VerifyOpenRouterProviderAsync`, after the existing successful content parse, add a second provider call that sends tools and tool results once the types exist:

```csharp
var toolRequest = new LlmRequest(
    Messages: new[]
    {
        LlmMessage.User("List files"),
        LlmMessage.ToolResult("call-1", "glob", "src/Aether/Program.cs")
    },
    Tools: new[]
    {
        new LlmTool(
            Name: "glob",
            Description: "Find files by pattern.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "pattern": { "type": "string" }
                  },
                  "required": ["pattern"]
                }
                """)
    });
```

This will not compile until the contracts are added.

- [ ] **Step 2: Run build to verify failure**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
```

Expected compile errors mention missing `LlmMessage.User`, `LlmMessage.ToolResult`, `LlmTool`, or `LlmRequest.Tools`.

- [ ] **Step 3: Replace provider contracts**

Replace `src/Aether/Providers/ILLMProvider.cs` with:

```csharp
namespace Aether.Providers;

public interface ILLMProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool>? Tools = null);

public sealed record LlmMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    string? ToolName = null)
{
    public static LlmMessage System(string content) => new("system", content);

    public static LlmMessage User(string content) => new("user", content);

    public static LlmMessage Assistant(string content) => new("assistant", content);

    public static LlmMessage ToolResult(string toolCallId, string toolName, string content)
    {
        return new LlmMessage("tool", content, toolCallId, toolName);
    }
}

public sealed record LlmTool(
    string Name,
    string Description,
    string ParametersJson);

public sealed record LlmResponse(
    string Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null);

public sealed record LlmToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);
```

- [ ] **Step 4: Fix call sites for new constructors**

Existing positional calls such as:

```csharp
new LlmRequest(new[] { new LlmMessage("user", "hello") })
```

remain valid. Existing response calls such as:

```csharp
new LlmResponse(_response)
```

remain valid because `ToolCalls` has a default.

Prefer gradually migrating new test code to factory methods, but do not churn existing code unless needed.

- [ ] **Step 5: Verify contract compile passes**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
```

Expected:

```text
Build succeeded.
```

Do not commit yet; Task 5 should make OpenRouter actually serialize and parse the new contract.

## Task 5: Teach OpenRouter Provider Tool Request/Response Shape

**Files:**
- Modify: `src/Aether/Providers/OpenRouterProvider.cs`
- Modify: `tests/Aether.Tests/Program.cs`
- Modify: `PROGRESS.md`

**Behavior:**

The provider should:

- Send tool definitions using OpenAI-compatible `tools`.
- Send tool result messages as role `tool`, with `tool_call_id`, `name`, and `content`.
- Parse `message.tool_calls[]` from OpenRouter responses.
- Allow empty assistant `content` when tool calls exist.

- [ ] **Step 1: Add failing OpenRouter serialization and parsing tests**

In `VerifyOpenRouterProviderAsync`, add a handler response for tool calls:

```csharp
var toolHandler = new CaptureHandler("""
    {
      "choices": [
        {
          "message": {
            "role": "assistant",
            "content": null,
            "tool_calls": [
              {
                "id": "call-1",
                "type": "function",
                "function": {
                  "name": "glob",
                  "arguments": "{\"pattern\":\"*.cs\",\"root\":\"src\"}"
                }
              }
            ]
          }
        }
      ]
    }
    """);
using var toolClient = new HttpClient(toolHandler)
{
    BaseAddress = new Uri("https://openrouter.ai/api/v1/")
};
var toolProvider = new OpenRouterProvider(
    toolClient,
    new OpenRouterOptions("test-key", "openai/gpt-test", "https://openrouter.ai/api/v1"));

var toolResponse = await toolProvider.CompleteAsync(toolRequest, CancellationToken.None);
Require(toolResponse.Content == "", "OpenRouterProvider must allow empty assistant content when tool calls are present.");
Require(toolResponse.ToolCalls is not null && toolResponse.ToolCalls.Count == 1, "OpenRouterProvider must parse tool calls.");
Require(toolResponse.ToolCalls![0].Id == "call-1", "OpenRouterProvider must parse tool call id.");
Require(toolResponse.ToolCalls[0].Name == "glob", "OpenRouterProvider must parse tool call name.");
Require(toolResponse.ToolCalls[0].Arguments["pattern"] == "*.cs", "OpenRouterProvider must parse tool call arguments.");
Require(toolHandler.LastBody.Contains("\"tools\"", StringComparison.Ordinal), "OpenRouterProvider must send tool definitions.");
Require(toolHandler.LastBody.Contains("\"role\":\"tool\"", StringComparison.Ordinal), "OpenRouterProvider must send tool result messages.");
Require(toolHandler.LastBody.Contains("\"tool_call_id\":\"call-1\"", StringComparison.Ordinal), "OpenRouterProvider must send tool call ids for tool results.");
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected failure:

```text
OpenRouterProvider must parse tool calls.
```

or an earlier provider content error because `content` is null.

- [ ] **Step 3: Serialize tools and tool messages**

In `OpenRouterProvider.cs`, replace the anonymous request body creation with:

```csharp
var body = new Dictionary<string, object?>
{
    ["model"] = _options.Model,
    ["messages"] = request.Messages.Select(ToOpenRouterMessage).ToArray()
};

if (request.Tools is { Count: > 0 })
{
    body["tools"] = request.Tools.Select(ToOpenRouterTool).ToArray();
}

httpRequest.Content = JsonContent.Create(body, options: JsonOptions);
```

Add:

```csharp
private static object ToOpenRouterMessage(LlmMessage message)
{
    if (message.Role == "tool")
    {
        return new
        {
            role = "tool",
            tool_call_id = message.ToolCallId,
            name = message.ToolName,
            content = message.Content
        };
    }

    return new
    {
        role = message.Role,
        content = message.Content
    };
}

private static object ToOpenRouterTool(LlmTool tool)
{
    using var parameters = JsonDocument.Parse(tool.ParametersJson);
    return new
    {
        type = "function",
        function = new
        {
            name = tool.Name,
            description = tool.Description,
            parameters = parameters.RootElement.Clone()
        }
    };
}
```

- [ ] **Step 4: Parse tool calls**

Replace content parsing in `CompleteAsync` with:

```csharp
var message = document.RootElement
    .GetProperty("choices")[0]
    .GetProperty("message");

var content = "";
if (message.TryGetProperty("content", out var contentElement)
    && contentElement.ValueKind != JsonValueKind.Null)
{
    content = contentElement.GetString() ?? "";
}

var toolCalls = ParseToolCalls(message);
if (string.IsNullOrWhiteSpace(content) && toolCalls.Count == 0)
{
    throw new InvalidOperationException("OpenRouter response did not contain assistant content or tool calls.");
}

return new LlmResponse(content, toolCalls);
```

Add:

```csharp
private static IReadOnlyList<LlmToolCall> ParseToolCalls(JsonElement message)
{
    if (!message.TryGetProperty("tool_calls", out var callsElement)
        || callsElement.ValueKind != JsonValueKind.Array)
    {
        return Array.Empty<LlmToolCall>();
    }

    var calls = new List<LlmToolCall>();
    foreach (var callElement in callsElement.EnumerateArray())
    {
        var id = callElement.GetProperty("id").GetString() ?? "";
        var function = callElement.GetProperty("function");
        var name = function.GetProperty("name").GetString() ?? "";
        var argumentsJson = function.GetProperty("arguments").GetString() ?? "{}";
        var arguments = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson, JsonOptions)
            ?? new Dictionary<string, string>();

        calls.Add(new LlmToolCall(id, name, arguments));
    }

    return calls;
}
```

- [ ] **Step 5: Verify and commit provider tool-call slice**

Run:

```bash
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
Build succeeded.
Aether Track B foundation smoke tests passed.
```

In `PROGRESS.md`, add:

```markdown
### Provider Tool-Call Contract Slice

Status: Completed

- Extended provider contracts with tool definitions, tool result messages, and assistant tool calls.
- Updated OpenRouter request serialization for OpenAI-compatible tools.
- Updated OpenRouter response parsing for assistant tool calls.
- Added smoke coverage for tool definitions, tool result messages, and parsed tool calls.
```

Run:

```bash
rtk git add src/Aether/Providers/ILLMProvider.cs src/Aether/Providers/OpenRouterProvider.cs tests/Aether.Tests/Program.cs PROGRESS.md
rtk git commit -m "feat: add provider tool call contracts"
```

## Final Verification For This Plan

Run:

```bash
rtk git status --short --branch
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln
rtk '/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj
```

Expected:

```text
## master
Build succeeded.
Aether Track B foundation smoke tests passed.
```

## Next Plan After This One

The next plan should implement the `AetherSoul` tool loop:

1. Define the built-in tool catalog sent to the model.
2. Convert `LlmToolCall` to `Agent.ToolCall`.
3. Execute each requested tool through `IToolExecutor`.
4. Append `LlmMessage.ToolResult(...)` messages.
5. Continue until the provider returns final assistant content with no tool calls.
6. Persist user, tool, and assistant turns safely in session history.

That next plan is the point where Aether crosses from "foundation with tools" into "small real agent."
