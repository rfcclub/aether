using System.Text.Json;
using System.Net;
using Aether.Agent;
using Aether.Channels;
using Aether.Data;
using Aether.Memory;
using Aether.Providers;
using Aether.Routing;
using Aether.Sessions;
using Aether.Skills;
using Aether.Agents;
using Microsoft.Extensions.Logging;
using Aether.Tests;

var root = FindRepoRoot();

RequireFile(root, "Aether.sln");
RequireFile(root, "src/Aether/Aether.csproj");
RequireFile(root, "src/Aether/Program.cs");
RequireFile(root, "src/Aether/appsettings.json");
RequireFile(root, "src/Aether/Data/Schema.sql");
RequireFile(root, "src/Aether/Data/AetherDb.cs");
RequireFile(root, "src/Aether/Channels/IChannel.cs");
RequireFile(root, "src/Aether/Channels/InboundMessage.cs");
RequireFile(root, "src/Aether/Routing/ChannelMessageQueue.cs");
RequireFile(root, "src/Aether/Routing/ChannelMessageQueue.cs");
RequireFile(root, "src/Aether/Routing/MessageRouter.cs");
RequireFile(root, "src/Aether/Routing/RoutedMessage.cs");
RequireFile(root, "src/Aether/Providers/ILLMProvider.cs");
RequireFile(root, "src/Aether/Providers/OpenRouterProvider.cs");
RequireFile(root, "src/Aether/Sessions/Session.cs");
RequireFile(root, "src/Aether/Sessions/SessionManager.cs");
RequireFile(root, "src/Aether/Memory/FileMemory.cs");
RequireFile(root, "src/Aether/Memory/FileMemory.cs");
RequireFile(root, "src/Aether/Agent/ToolExecutor.cs");
RequireFile(root, "src/Aether/Agent/DisabledToolExecutor.cs");
RequireFile(root, "src/Aether/Agent/ToolExecutor.cs");
RequireFile(root, "src/Aether/Agent/AetherSoul.cs");
RequireFile(root, "PROGRESS.md");

RequireDirectory(root, "src/Aether/Channels");
RequireDirectory(root, "src/Aether/Routing");
RequireDirectory(root, "src/Aether/Data");
RequireDirectory(root, "src/Aether/Agent");
RequireDirectory(root, "src/Aether/Providers");
RequireDirectory(root, "src/Aether/Memory");
RequireDirectory(root, "src/Aether/Sessions");
RequireDirectory(root, "src/Aether/Scheduler");

var project = File.ReadAllText(Path.Combine(root, "src/Aether/Aether.csproj"));
Require(project.Contains("<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal), "Aether.csproj must target net10.0.");

var appsettingsPath = Path.Combine(root, "src/Aether/appsettings.json");
using var appsettings = JsonDocument.Parse(File.ReadAllText(appsettingsPath));
Require(appsettings.RootElement.TryGetProperty("assistant", out _), "appsettings.json must contain assistant settings.");
Require(appsettings.RootElement.TryGetProperty("channels", out _), "appsettings.json must contain channel settings.");
Require(appsettings.RootElement.TryGetProperty("llm", out _), "appsettings.json must contain llm settings.");
Require(appsettings.RootElement.TryGetProperty("sandbox", out _), "appsettings.json must contain sandbox settings.");

var schema = File.ReadAllText(Path.Combine(root, "src/Aether/Data/Schema.sql"));
foreach (var table in new[] { "messages", "sessions", "tasks", "groups", "task_runs" })
{
    Require(
        schema.Contains($"CREATE TABLE IF NOT EXISTS {table}", StringComparison.OrdinalIgnoreCase),
        $"Schema.sql must create the {table} table.");
}

var progress = File.ReadAllText(Path.Combine(root, "PROGRESS.md"));
Require(progress.Contains("Status: Completed", StringComparison.Ordinal), "PROGRESS.md must mark verified scaffold work completed.");
Require(progress.Contains("Next Steps", StringComparison.Ordinal), "PROGRESS.md must contain next steps.");

await VerifyAetherDbAsync(root);
await VerifyMessageQueueAsync();
await VerifyMessageRouterAsync(root);
await VerifyOpenRouterProviderAsync();
await VerifySessionManagerAsync(root);
await VerifyFileMemoryAsync();
await VerifyToolExecutorAsync();
await VerifyAetherSoulAsync(root);

Console.WriteLine("Aether Track B foundation smoke tests passed.");

static async Task VerifyAetherDbAsync(string root)
{
    var databasePath = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid():N}.db");
    var schemaPath = Path.Combine(root, "src/Aether/Data/Schema.sql");

    try
    {
        var db = new AetherDb(databasePath, schemaPath);
        await db.InitializeAsync(CancellationToken.None);
        await db.InitializeAsync(CancellationToken.None);

        foreach (var table in new[] { "messages", "sessions", "tasks", "groups", "task_runs" })
        {
            Require(await db.TableExistsAsync(table, CancellationToken.None), $"AetherDb must create the {table} table.");
        }

        var expected = new GroupRoute("telegram:12345", "main", true, "@Aether");
        await db.UpsertGroupRouteAsync(expected, CancellationToken.None);

        var actual = await db.GetGroupRouteAsync("telegram:12345", CancellationToken.None);
        Require(actual is not null, "AetherDb must return registered group routes.");
        var actualRoute = actual.GetValueOrDefault();
        Require(actualRoute.Folder == expected.Folder, "AetherDb must preserve group folder.");
        Require(actualRoute.IsMain == expected.IsMain, "AetherDb must preserve main-group flag.");
        Require(actualRoute.Trigger == expected.Trigger, "AetherDb must preserve trigger.");
    }
    finally
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}

static async Task VerifyMessageQueueAsync()
{
    var queue = new ChannelMessageQueue();
    var inbound = new InboundMessage(
        Id: "msg-1",
        ChannelName: "telegram",
        ChatId: "12345",
        SenderId: "thoor",
        Text: "hello",
        Timestamp: DateTimeOffset.UnixEpoch);
    var routed = new RoutedMessage(inbound, "main", "main", "hello");

    await queue.EnqueueAsync(routed, CancellationToken.None);
    var dequeued = await queue.ReadAsync(CancellationToken.None);

    Require(dequeued.WorkspacePath == "main", "ChannelMessageQueue must preserve group folder.");
    Require(dequeued.Prompt == "hello", "ChannelMessageQueue must preserve prompt.");
}

static async Task VerifyMessageRouterAsync(string root)
{
    var databasePath = Path.Combine(Path.GetTempPath(), $"aether-router-test-{Guid.NewGuid():N}.db");
    var schemaPath = Path.Combine(root, "src/Aether/Data/Schema.sql");

    try
    {
        var db = new AetherDb(databasePath, schemaPath);
        await db.InitializeAsync(CancellationToken.None);
        await db.UpsertGroupRouteAsync(new GroupRoute("telegram:12345", "main", true, "@Aether"), CancellationToken.None);

        var queue = new ChannelMessageQueue();
        var router = new MessageRouter(db, queue);
        var inbound = new InboundMessage(
            Id: "msg-2",
            ChannelName: "telegram",
            ChatId: "12345",
            SenderId: "thoor",
            Text: "  @Aether status  ",
            Timestamp: DateTimeOffset.UnixEpoch);

        var routed = await router.RouteAsync(inbound, CancellationToken.None);
        Require(routed is not null, "MessageRouter must route registered groups.");
        var routedMessage = routed.GetValueOrDefault();
        Require(routedMessage.WorkspacePath == "main", "MessageRouter must preserve group folder.");
        Require(routedMessage.Prompt == "status", "MessageRouter must trim trigger words from prompts.");

        var dequeued = await queue.ReadAsync(CancellationToken.None);
        Require(dequeued.Prompt == "status", "MessageRouter must enqueue routed messages.");
    }
    finally
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}

static async Task VerifyOpenRouterProviderAsync()
{
    var handler = new CaptureHandler("""
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "pong"
              }
            }
          ]
        }
        """);
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri("https://openrouter.ai/api/v1/")
    };
    var provider = new OpenRouterProvider(
        client,
        new OpenRouterOptions("test-key", "openai/gpt-test", "https://openrouter.ai/api/v1"));

    var response = await provider.CompleteAsync(
        new LlmRequest(
            new[]
            {
                new LlmMessage("system", "You are Aether."),
                new LlmMessage("user", "ping")
            }),
        CancellationToken.None);

    Require(response.Content == "pong", "OpenRouterProvider must parse assistant content.");
    Require(handler.LastRequest is not null, "OpenRouterProvider must send an HTTP request.");
    Require(handler.LastRequest!.RequestUri!.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal), "OpenRouterProvider must call chat completions.");
    Require(handler.LastRequest.Headers.Authorization?.Scheme == "Bearer", "OpenRouterProvider must use bearer authorization.");
    Require(handler.LastBody.Contains("\"model\":\"openai/gpt-test\"", StringComparison.Ordinal), "OpenRouterProvider must send the configured model.");
    Require(handler.LastBody.Contains("\"role\":\"user\"", StringComparison.Ordinal), "OpenRouterProvider must send chat messages.");

    var toolRequest = new LlmRequest(
        Messages: new[]
        {
            LlmMessage.User("List files"),
            LlmMessage.AssistantToolCalls(
                "",
                new[]
                {
                    new LlmToolCall(
                        Id: "call-1",
                        Name: "glob",
                        Arguments: new Dictionary<string, string> { ["pattern"] = "*.cs" })
                }),
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
    Require(toolHandler.LastBody.Contains("\"tool_calls\"", StringComparison.Ordinal), "OpenRouterProvider must send assistant tool-call messages.");
    Require(toolHandler.LastBody.Contains("\"role\":\"tool\"", StringComparison.Ordinal), "OpenRouterProvider must send tool result messages.");
    Require(toolHandler.LastBody.Contains("\"tool_call_id\":\"call-1\"", StringComparison.Ordinal), "OpenRouterProvider must send tool call ids for tool results.");

    var errorHandler = new CaptureHandler("""{"error":{"message":"model is unavailable"}}""", HttpStatusCode.BadRequest);
    using var errorClient = new HttpClient(errorHandler)
    {
        BaseAddress = new Uri("https://openrouter.ai/api/v1/")
    };
    var errorProvider = new OpenRouterProvider(
        errorClient,
        new OpenRouterOptions("test-key", "openrouter/free", "https://openrouter.ai/api/v1"));

    try
    {
        await errorProvider.CompleteAsync(
            new LlmRequest(new[] { new LlmMessage("user", "hello") }),
            CancellationToken.None);
        throw new InvalidOperationException("OpenRouterProvider must throw on non-success responses.");
    }
    catch (InvalidOperationException ex)
    {
        Require(ex.Message.Contains("400", StringComparison.Ordinal), "OpenRouterProvider errors must include HTTP status.");
        Require(ex.Message.Contains("model is unavailable", StringComparison.Ordinal), "OpenRouterProvider errors must include response body.");
    }
}

static async Task VerifySessionManagerAsync(string root)
{
    var databasePath = Path.Combine(Path.GetTempPath(), $"aether-session-test-{Guid.NewGuid():N}.db");
    var schemaPath = Path.Combine(root, "src/Aether/Data/Schema.sql");

    try
    {
        var db = new AetherDb(databasePath, schemaPath);
        await db.InitializeAsync(CancellationToken.None);

        var sessions = new SessionManager(db);
        var session = await sessions.GetOrCreateSessionAsync("main", CancellationToken.None);
        await sessions.AppendMessageAsync(session.Id, new SessionMessage("user", "hello", DateTimeOffset.UnixEpoch), CancellationToken.None);
        await sessions.AppendMessageAsync(session.Id, new SessionMessage("assistant", "hi", DateTimeOffset.UnixEpoch.AddSeconds(1)), CancellationToken.None);

        var history = await sessions.GetHistoryAsync(session.Id, maxMessages: 10, CancellationToken.None);
        Require(history.Count == 2, "SessionManager must return saved history.");
        Require(history[0].Role == "user", "SessionManager must preserve user message role.");
        Require(history[1].Content == "hi", "SessionManager must preserve assistant message content.");

        var sameSession = await sessions.GetOrCreateSessionAsync("main", CancellationToken.None);
        Require(sameSession.Id == session.Id, "SessionManager must reuse the active session for a group.");
    }
    finally
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}

static async Task VerifyFileMemoryAsync()
{
    var memoryRoot = Path.Combine(Path.GetTempPath(), $"aether-memory-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(memoryRoot, "main"));

    try
    {
        await File.WriteAllTextAsync(Path.Combine(memoryRoot, "CLAUDE.md"), "global memory");
        await File.WriteAllTextAsync(Path.Combine(memoryRoot, "main", "CLAUDE.md"), "main memory");

        var memory = new FileMemory(memoryRoot);
        var context = await memory.LoadContextAsync("main", CancellationToken.None);

        Require(context.Contains("global memory", StringComparison.Ordinal), "FileMemory must load global CLAUDE.md.");
        Require(context.Contains("main memory", StringComparison.Ordinal), "FileMemory must load group CLAUDE.md.");
    }
    finally
    {
        if (Directory.Exists(memoryRoot))
        {
            Directory.Delete(memoryRoot, recursive: true);
        }
    }
}

static async Task VerifyToolExecutorAsync()
{
    var sandboxRoot = Path.Combine(Path.GetTempPath(), $"aether-tools-test-{Guid.NewGuid():N}");
    var outsideRoot = Path.Combine(Path.GetTempPath(), $"aether-tools-outside-{Guid.NewGuid():N}");
    Directory.CreateDirectory(sandboxRoot);
    Directory.CreateDirectory(outsideRoot);

    try
    {
        var notesPath = Path.Combine(sandboxRoot, "notes.txt");
        var nested = Path.Combine(sandboxRoot, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(notesPath, "alpha\nbeta needle\ngamma\n");
        await File.WriteAllTextAsync(Path.Combine(nested, "match.md"), "needle in nested file\n");
        var outsidePath = Path.Combine(outsideRoot, "secret.txt");
        await File.WriteAllTextAsync(outsidePath, "do not read");

        var executor = new ToolExecutor(new SandboxOptions(
            Type: "process",
            TimeoutMs: 1000,
            MaxMemoryMb: 128,
            NetworkEnabled: false,
            AllowedPaths: new[] { sandboxRoot }));

        var read = await executor.ExecuteAsync(
            new ToolCall("read", new Dictionary<string, string> { ["path"] = notesPath }),
            CancellationToken.None);
        Require(read.Succeeded, "ToolExecutor read must succeed for files inside allowed paths.");
        Require(read.Output.Contains("beta needle", StringComparison.Ordinal), "ToolExecutor read must return file contents.");

        var denied = await executor.ExecuteAsync(
            new ToolCall("read", new Dictionary<string, string> { ["path"] = outsidePath }),
            CancellationToken.None);
        Require(!denied.Succeeded, "ToolExecutor read must reject files outside allowed paths.");
        Require(denied.Error == "Path not permitted", "ToolExecutor read rejection must use the expected error.");

        var glob = await executor.ExecuteAsync(
            new ToolCall("glob", new Dictionary<string, string>
            {
                ["root"] = sandboxRoot,
                ["pattern"] = "*.md"
            }),
            CancellationToken.None);
        Require(glob.Succeeded, "ToolExecutor glob must succeed for allowed roots.");
        Require(glob.Output.Contains("match.md", StringComparison.Ordinal), "ToolExecutor glob must return matching files.");

        var grep = await executor.ExecuteAsync(
            new ToolCall("grep", new Dictionary<string, string>
            {
                ["path"] = sandboxRoot,
                ["pattern"] = "needle"
            }),
            CancellationToken.None);
        Require(grep.Succeeded, "ToolExecutor grep must succeed for allowed paths.");
        Require(grep.Output.Contains("notes.txt:2:beta needle", StringComparison.Ordinal), "ToolExecutor grep must include file, line number, and match.");
        Require(grep.Output.Contains("match.md:1:needle in nested file", StringComparison.Ordinal), "ToolExecutor grep must search nested files.");

        var grepWithContext = await executor.ExecuteAsync(
            new ToolCall("grep", new Dictionary<string, string>
            {
                ["path"] = notesPath,
                ["pattern"] = "needle",
                ["context_lines"] = "1"
            }),
            CancellationToken.None);
        Require(grepWithContext.Succeeded, "ToolExecutor grep must support context lines.");
        Require(grepWithContext.Output.Contains("notes.txt:1-alpha", StringComparison.Ordinal), "ToolExecutor grep must include before-context lines.");
        Require(grepWithContext.Output.Contains("notes.txt:3-gamma", StringComparison.Ordinal), "ToolExecutor grep must include after-context lines.");

        var bash = await executor.ExecuteAsync(
            new ToolCall("bash", new Dictionary<string, string>
            {
                ["cwd"] = sandboxRoot,
                ["command"] = ShellEchoCommand("hello from bash")
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
                ["command"] = ShellStderrAndExitCommand("bad news", 7)
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
                ["command"] = ShellSleepCommand(2)
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
                ["command"] = ShellEchoCommand("abcdefghijklmnopqrstuvwxyz")
            }),
            CancellationToken.None);
        Require(bashTruncated.Succeeded, "ToolExecutor bash must succeed even when output is truncated.");
        Require(bashTruncated.Output.Contains("[truncated]", StringComparison.Ordinal), "ToolExecutor bash must mark truncated output.");

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

        var unknown = await executor.ExecuteAsync(
            new ToolCall("nope", new Dictionary<string, string>()),
            CancellationToken.None);
        Require(!unknown.Succeeded, "ToolExecutor must reject unknown tools.");
        Require(unknown.Error == "Unknown tool: nope", "ToolExecutor unknown tool error must name the tool.");
    }
    finally
    {
        if (Directory.Exists(sandboxRoot))
        {
            Directory.Delete(sandboxRoot, recursive: true);
        }

        if (Directory.Exists(outsideRoot))
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }
}

static string ShellEchoCommand(string text)
{
    return OperatingSystem.IsWindows()
        ? $"echo {text}"
        : $"printf '{text}'";
}

static string ShellStderrAndExitCommand(string text, int exitCode)
{
    return OperatingSystem.IsWindows()
        ? $"echo {text} 1>&2 & exit /b {exitCode}"
        : $"printf '{text}' >&2; exit {exitCode}";
}

static string ShellSleepCommand(int seconds)
{
    return OperatingSystem.IsWindows()
        ? $"powershell -NoProfile -Command \"Start-Sleep -Seconds {seconds}\""
        : $"sleep {seconds}";
}

static async Task VerifyAetherSoulAsync(string root)
{
    var databasePath = Path.Combine(Path.GetTempPath(), $"aether-soul-test-{Guid.NewGuid():N}.db");
    var schemaPath = Path.Combine(root, "src/Aether/Data/Schema.sql");
    var memoryRoot = Path.Combine(Path.GetTempPath(), $"aether-soul-memory-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(memoryRoot, "main"));

    try
    {
        await File.WriteAllTextAsync(Path.Combine(memoryRoot, "CLAUDE.md"), "global instructions");
        await File.WriteAllTextAsync(Path.Combine(memoryRoot, "main", "CLAUDE.md"), "main instructions");

        var db = new AetherDb(databasePath, schemaPath);
        await db.InitializeAsync(CancellationToken.None);

        var provider = new FakeProvider("ack");
        var tools = new DisabledToolExecutor();
        var soul = new AetherSoul(provider, tools, TestAgentProfile.NoOp());

        var response = await soul.ProcessAsync("main", "hello", CancellationToken.None);

        Require(response.Content == "ack", "AetherSoul must return provider content.");
        Require(provider.LastRequest is not null, "AetherSoul must call the LLM provider.");
        Require(provider.LastRequest!.Messages.Any(m => m.Role == "system" && m.Content.Contains("You are Aether", StringComparison.Ordinal)), "AetherSoul must include identity context.");

        var toolProvider = new FakeProvider(
            new LlmResponse(
                "",
                new[]
                {
                    new LlmToolCall(
                        Id: "call-1",
                        Name: "read",
                        Arguments: new Dictionary<string, string> { ["path"] = "notes.txt" })
                }),
            new LlmResponse("tool result final"));
        var toolExecutor = new CapturingToolExecutor(new ToolResult(true, "file contents"));
        var toolSoul = new AetherSoul(toolProvider, toolExecutor, TestAgentProfile.NoOp());

        var toolResponse = await toolSoul.ProcessAsync("tools", "inspect notes", CancellationToken.None);

        Require(toolResponse.Content == "tool result final", "AetherSoul must return final provider content after tool calls.");
        Require(toolProvider.Requests.Count == 2, "AetherSoul must continue the LLM loop after a tool call.");
        Require(toolProvider.Requests[0].Tools is { Count: > 0 }, "AetherSoul must send built-in tool definitions to the provider.");
        Require(toolProvider.Requests[0].Tools!.Any(tool => tool.Name == "read"), "AetherSoul must include the read tool definition.");
        Require(toolExecutor.Calls.Count == 1, "AetherSoul must execute requested tool calls.");
        Require(toolExecutor.Calls[0].Name == "read", "AetherSoul must execute the requested tool name.");
        Require(toolProvider.Requests[1].Messages.Any(message =>
            message.Role == "tool"
            && message.ToolCallId == "call-1"
            && message.ToolName == "read"
            && message.Content.Contains("file contents", StringComparison.Ordinal)), "AetherSoul must send tool results back to the provider.");
    }
    finally
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        if (Directory.Exists(memoryRoot))
        {
            Directory.Delete(memoryRoot, recursive: true);
        }
    }
}

static string FindRepoRoot()
{
    var directory = AppContext.BaseDirectory;

    while (!string.IsNullOrWhiteSpace(directory))
    {
        if (File.Exists(Path.Combine(directory, "PLAN.md")) && File.Exists(Path.Combine(directory, "ARCHITECTURE.md")))
        {
            return directory;
        }

        var parent = Directory.GetParent(directory);
        if (parent is null)
        {
            break;
        }

        directory = parent.FullName;
    }

    throw new InvalidOperationException("Could not find Aether repository root.");
}

static void RequireFile(string root, string relativePath)
{
    Require(File.Exists(Path.Combine(root, relativePath)), $"Expected file '{relativePath}' to exist.");
}

static void RequireDirectory(string root, string relativePath)
{
    Require(Directory.Exists(Path.Combine(root, relativePath)), $"Expected directory '{relativePath}' to exist.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class CaptureHandler : HttpMessageHandler
{
    private readonly string _responseJson;
    private readonly HttpStatusCode _statusCode;

    public CaptureHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseJson = responseJson;
        _statusCode = statusCode;
    }

    public HttpRequestMessage? LastRequest { get; private set; }
    public string LastBody { get; private set; } = "";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseJson)
        };
    }
}

internal sealed class FakeProvider : ILLMProvider
{
    private readonly Queue<LlmResponse> _responses;

    public string Name => "fake";
    public string Model => "fake-model";
    public bool SupportsStreaming => false;
    public bool SupportsTools => true;

    public FakeProvider(string response)
        : this(new LlmResponse(response))
    {
    }

    public LlmRequest? LastRequest { get; private set; }
    public List<LlmRequest> Requests { get; } = new();

    public FakeProvider(params LlmResponse[] responses)
    {
        _responses = new Queue<LlmResponse>(responses);
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        LastRequest = request;
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("FakeProvider has no more responses.");
        }

        return Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return response.Content;
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return new StreamEvent.TextToken(response.Content);
        yield return new StreamEvent.Response(response);
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct) => Task.FromResult(true);
}

internal sealed class CapturingToolExecutor : ToolExecutor
{
    private readonly ToolResult _result;

    public CapturingToolExecutor(ToolResult result) : base()
    {
        _result = result;
    }

    public List<ToolCall> Calls { get; } = new();

    public override Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        Calls.Add(call);
        return Task.FromResult(_result);
    }
}
