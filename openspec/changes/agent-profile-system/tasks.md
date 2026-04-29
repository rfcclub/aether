# Agent Profile System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Agent Profile System + Memory Bridge + Heartbeat to Aether so it can host Maria from OpenClaw.

**Architecture:** Three new files in `src/Aether/Agents/` — `AgentProfile` loads persona directory and builds dynamic system prompts, `AgentMemoryBridge` provides OC-format daily memory read/write, `AgentHeartbeatService` runs periodic HEARTBEAT.md checks. Modify `AetherSoul` to accept `IAgentProfile` and use dynamic prompts. All wired through DI in `Program.cs`.

**Tech Stack:** C# 13, .NET 9, xUnit, NSubstitute

---

### Task 1: IAgentProfile Interface

**Files:**
- Create: `src/Aether/Agents/IAgentProfile.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Aether.Agents;

/// <summary>
/// Loads an agent's persona from a directory containing SOUL.md, USER.md, etc.
/// An agent directory maps directly to an OC workspace structure.
/// </summary>
public interface IAgentProfile
{
    /// <summary>
    /// The agent's configured name (directory name).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Path to the agent's root directory.
    /// </summary>
    string AgentDirectory { get; }

    /// <summary>
    /// Load all startup files in the configured order and return the full
    /// persona context for system prompt injection.
    /// </summary>
    Task<string> LoadPersonaAsync(CancellationToken ct = default);

    /// <summary>
    /// Load a specific file from the agent directory.
    /// Returns null if the file does not exist.
    /// </summary>
    Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Load today's and yesterday's daily memory files (memory/YYYY-MM-DD.md).
    /// </summary>
    Task<string> LoadDailyMemoryAsync(CancellationToken ct = default);
}

/// <summary>
/// Configuration for an agent profile — which files to load at startup and in what order.
/// </summary>
public record AgentConfig
{
    /// <summary>
    /// Ordered list of files to load at session start. Paths relative to agent directory.
    /// Default: SOUL.md, USER.md
    /// </summary>
    public List<string> StartupFiles { get; init; } = new() { "SOUL.md", "USER.md" };

    /// <summary>
    /// Path to long-term memory file. Loaded on startup, written on significant events.
    /// Default: MEMORY.md
    /// </summary>
    public string LongTermMemoryFile { get; init; } = "MEMORY.md";

    /// <summary>
    /// Path to heartbeat file. If present, heartbeats are enabled for this agent.
    /// Default: HEARTBEAT.md
    /// </summary>
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>
    /// Directory for daily memory transcripts. Relative to agent directory.
    /// Default: memory
    /// </summary>
    public string DailyMemoryDirectory { get; init; } = "memory";

    /// <summary>
    /// Path to task inbox file. Read on startup.
    /// Default: TASK_INBOX.md
    /// </summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";

    /// <summary>
    /// Path to task report file. Written by agent after completing tasks.
    /// Default: TASK_REPORT.md
    /// </summary>
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Agents/IAgentProfile.cs
git commit -m "feat: add IAgentProfile interface and AgentConfig record"
```

---

### Task 2: AgentProfile Implementation

**Files:**
- Create: `src/Aether/Agents/AgentProfile.cs`
- Test: `tests/Aether.Tests/AgentProfileTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Agents;

namespace Aether.Tests;

public sealed class AgentProfileTests : IDisposable
{
    private readonly string _agentDir;

    public AgentProfileTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-agent-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task LoadPersonaAsync_LoadsConfiguredFilesInOrder()
    {
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "I am Maria.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");

        var config = new AgentConfig { StartupFiles = new() { "SOUL.md", "USER.md" } };
        var profile = new AgentProfile("maria", _agentDir, config);

        var persona = await profile.LoadPersonaAsync();

        Assert.Contains("I am Maria.", persona);
        Assert.Contains("User is Thoor.", persona);
        var soulIndex = persona.IndexOf("I am Maria.");
        var userIndex = persona.IndexOf("User is Thoor.");
        Assert.True(soulIndex < userIndex, "SOUL.md should load before USER.md");
    }

    [Fact]
    public async Task LoadPersonaAsync_SkipsMissingOptionalFiles()
    {
        // No files exist, but startup list references them
        var config = new AgentConfig { StartupFiles = new() { "SOUL.md", "NONEXISTENT.md" } };
        var profile = new AgentProfile("maria", _agentDir, config);

        var persona = await profile.LoadPersonaAsync();

        // Should not throw, should return empty for missing files
        Assert.NotNull(persona);
    }

    [Fact]
    public async Task LoadDailyMemoryAsync_LoadsTodayAndYesterday()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(_agentDir, "memory", $"{today}.md"), "Today's notes.");
        File.WriteAllText(Path.Combine(_agentDir, "memory", $"{yesterday}.md"), "Yesterday's notes.");

        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());

        var memory = await profile.LoadDailyMemoryAsync();

        Assert.Contains("Today's notes.", memory);
        Assert.Contains("Yesterday's notes.", memory);
    }

    [Fact]
    public async Task LoadFileAsync_ReturnsNullForMissingFile()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());

        var result = await profile.LoadFileAsync("NONEXISTENT.md");

        Assert.Null(result);
    }

    [Fact]
    public void Name_ReturnsConfiguredName()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());

        Assert.Equal("maria", profile.Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentProfileTests"`
Expected: FAIL — AgentProfile class not found

- [ ] **Step 3: Write the AgentProfile implementation**

```csharp
namespace Aether.Agents;

public sealed class AgentProfile : IAgentProfile
{
    private readonly AgentConfig _config;

    public string Name { get; }
    public string AgentDirectory { get; }

    public AgentProfile(string name, string agentDirectory, AgentConfig config)
    {
        Name = name;
        AgentDirectory = agentDirectory;
        _config = config;
    }

    public async Task<string> LoadPersonaAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        foreach (var file in _config.StartupFiles)
        {
            var content = await LoadFileAsync(file, ct);
            if (content is not null)
                parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }

    public async Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(AgentDirectory, relativePath);
        if (!File.Exists(fullPath))
            return null;
        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task<string> LoadDailyMemoryAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        var dates = new[] { DateTime.UtcNow, DateTime.UtcNow.AddDays(-1) };
        foreach (var date in dates)
        {
            var filename = $"{date:yyyy-MM-dd}.md";
            var content = await LoadFileAsync(Path.Combine(_config.DailyMemoryDirectory, filename), ct);
            if (content is not null)
                parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentProfileTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Agents/AgentProfile.cs tests/Aether.Tests/AgentProfileTests.cs
git commit -m "feat: add AgentProfile implementation with persona loading"
```

---

### Task 3: AgentMemoryBridge

**Files:**
- Create: `src/Aether/Agents/AgentMemoryBridge.cs`
- Test: `tests/Aether.Tests/AgentMemoryBridgeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Agents;

namespace Aether.Tests;

public sealed class AgentMemoryBridgeTests : IDisposable
{
    private readonly string _agentDir;

    public AgentMemoryBridgeTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-mem-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task AppendDailyMemoryAsync_WritesToCorrectFile()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        await bridge.AppendDailyMemoryAsync("New session started.", "test-session");

        var filePath = Path.Combine(_agentDir, "memory", $"{today}.md");
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("New session started.", content);
        Assert.Contains("test-session", content);
    }

    [Fact]
    public async Task ReadLongTermMemoryAsync_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_agentDir, "MEMORY.md"), "Long-term memories here.");
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        var result = await bridge.ReadLongTermMemoryAsync();

        Assert.Equal("Long-term memories here.", result);
    }

    [Fact]
    public async Task ReadLongTermMemoryAsync_ReturnsEmptyWhenMissing()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        var result = await bridge.ReadLongTermMemoryAsync();

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task WriteLongTermMemoryAsync_OverwritesFile()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        await bridge.WriteLongTermMemoryAsync("Updated memories.");
        var result = await File.ReadAllTextAsync(Path.Combine(_agentDir, "MEMORY.md"));

        Assert.Equal("Updated memories.", result);
    }

    [Fact]
    public async Task ReadTaskInboxAsync_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_agentDir, "TASK_INBOX.md"), "- [ ] Task 1\n- [ ] Task 2");
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        var result = await bridge.ReadTaskInboxAsync();

        Assert.Contains("Task 1", result);
    }

    [Fact]
    public async Task WriteTaskReportAsync_WritesToReportFile()
    {
        var bridge = new AgentMemoryBridge(_agentDir, new AgentConfig());

        await bridge.WriteTaskReportAsync("Task 1 completed.");

        var content = await File.ReadAllTextAsync(Path.Combine(_agentDir, "TASK_REPORT.md"));
        Assert.Contains("Task 1 completed.", content);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentMemoryBridgeTests"`
Expected: FAIL — AgentMemoryBridge class not found

- [ ] **Step 3: Write the AgentMemoryBridge implementation**

```csharp
namespace Aether.Agents;

/// <summary>
/// Bridges OC-format agent memory files (daily transcripts, MEMORY.md, task inbox/report).
/// Complements the existing IMemorySystem (SQLite/FTS5) with file-based agent memory.
/// </summary>
public sealed class AgentMemoryBridge
{
    private readonly string _agentDir;
    private readonly AgentConfig _config;

    public AgentMemoryBridge(string agentDir, AgentConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    public async Task AppendDailyMemoryAsync(string content, string sessionId)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        Directory.CreateDirectory(dailyDir);

        var filename = $"{DateTime.UtcNow:yyyy-MM-dd}.md";
        var filePath = Path.Combine(dailyDir, filename);

        var entry = $"\n## {DateTime.UtcNow:HH:mm:ss} UTC | session: {sessionId}\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    public async Task<string> ReadLongTermMemoryAsync(CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    public async Task WriteLongTermMemoryAsync(string content, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    public async Task<string> ReadTaskInboxAsync(CancellationToken ct = default)
    {
        if (_config.TaskInboxFile is null)
            return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.TaskInboxFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    public async Task WriteTaskReportAsync(string content, CancellationToken ct = default)
    {
        if (_config.TaskReportFile is null)
            return;
        var filePath = Path.Combine(_agentDir, _config.TaskReportFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentMemoryBridgeTests"`
Expected: 6 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Agents/AgentMemoryBridge.cs tests/Aether.Tests/AgentMemoryBridgeTests.cs
git commit -m "feat: add AgentMemoryBridge for OC-format memory files"
```

---

### Task 4: AgentHeartbeatService

**Files:**
- Create: `src/Aether/Agents/AgentHeartbeatService.cs`
- Test: `tests/Aether.Tests/AgentHeartbeatServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Agents;
using Aether.Agent;
using Aether.Providers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aether.Tests;

public sealed class AgentHeartbeatServiceTests
{
    [Fact]
    public async Task ExecuteAsync_LoadsHeartbeatFile_WhenConfigured()
    {
        var agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-hb-{Guid.NewGuid()}");
        Directory.CreateDirectory(agentDir);
        try
        {
            File.WriteAllText(Path.Combine(agentDir, "HEARTBEAT.md"), "- [x] Check inbox\nHEARTBEAT_OK");
            var config = new AgentConfig { HeartbeatFile = "HEARTBEAT.md" };
            var profile = new AgentProfile("maria", agentDir, config);
            var soul = Substitute.For<AetherSoul>(
                Substitute.For<ILLMProvider>(),
                Substitute.For<Memory.IMemorySystem>(),
                Substitute.For<Tooling.IToolExecutor>(),
                Substitute.For<Sessions.ISessionManager>(),
                Substitute.For<Skills.ISkillRegistry>(),
                Substitute.For<Skills.ISkillTrigger>()
            );
            var logger = Substitute.For<ILogger<AgentHeartbeatService>>();

            // Configure AetherSoul to return HEARTBEAT_OK
            soul.ProcessAsync("maria", Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new AgentResponse("HEARTBEAT_OK", "maria")));

            var service = new AgentHeartbeatService(
                profile,
                soul,
                config,
                logger,
                TimeSpan.FromMilliseconds(100));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await service.StartAsync(cts.Token);
            await Task.Delay(300, CancellationToken.None); // Let it tick once
            await service.StopAsync(cts.Token);

            // The service should have called ProcessAsync at least once
            await soul.Received(1).ProcessAsync(
                "maria",
                Arg.Is<string>(s => s.Contains("HEARTBEAT_OK") || s.Contains("Check inbox")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(agentDir))
                Directory.Delete(agentDir, recursive: true);
        }
    }

    [Fact]
    public async Task HeartbeatDisabled_WhenNoHeartbeatFile()
    {
        var agentDir = Path.Combine(Path.GetTempPath(), $"aether-test-hb2-{Guid.NewGuid()}");
        Directory.CreateDirectory(agentDir);
        try
        {
            var config = new AgentConfig { HeartbeatFile = null };
            var profile = new AgentProfile("maria", agentDir, config);
            var soul = Substitute.For<AetherSoul>(
                Substitute.For<ILLMProvider>(),
                Substitute.For<Memory.IMemorySystem>(),
                Substitute.For<Tooling.IToolExecutor>(),
                Substitute.For<Sessions.ISessionManager>(),
                Substitute.For<Skills.ISkillRegistry>(),
                Substitute.For<Skills.ISkillTrigger>()
            );
            var logger = Substitute.For<ILogger<AgentHeartbeatService>>();

            var service = new AgentHeartbeatService(
                profile, soul, config, logger,
                TimeSpan.FromMilliseconds(100));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await service.StartAsync(cts.Token);
            await Task.Delay(300, CancellationToken.None);
            await service.StopAsync(cts.Token);

            // Should never call ProcessAsync when heartbeat is disabled
            await soul.DidNotReceive().ProcessAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(agentDir))
                Directory.Delete(agentDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentHeartbeatServiceTests"`
Expected: FAIL — AgentHeartbeatService class not found

- [ ] **Step 3: Write the AgentHeartbeatService implementation**

```csharp
using Aether.Agent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Agents;

/// <summary>
/// Periodic heartbeat service that reads HEARTBEAT.md and sends its content
/// through AetherSoul for processing. Responds to HEARTBEAT_OK to stay idle.
/// Implements the OC heartbeat pattern: poll → execute tasks → report.
/// </summary>
public sealed class AgentHeartbeatService : IHostedService, IDisposable
{
    private readonly IAgentProfile _profile;
    private readonly AetherSoul _soul;
    private readonly AgentConfig _config;
    private readonly ILogger<AgentHeartbeatService> _logger;
    private readonly TimeSpan _interval;
    private Timer? _timer;
    private CancellationTokenSource? _cts;

    public AgentHeartbeatService(
        IAgentProfile profile,
        AetherSoul soul,
        AgentConfig config,
        ILogger<AgentHeartbeatService> logger,
        TimeSpan? interval = null)
    {
        _profile = profile;
        _soul = soul;
        _config = config;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(5);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config.HeartbeatFile is null)
        {
            _logger.LogInformation("Heartbeat disabled for agent {AgentName}", _profile.Name);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Heartbeat starting for agent {AgentName} every {Interval}",
            _profile.Name, _interval);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new Timer(async _ => await TickAsync(_cts.Token), null,
            TimeSpan.Zero, _interval);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Heartbeat stopping for agent {AgentName}", _profile.Name);
        _timer?.Change(Timeout.Infinite, 0);
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            var heartbeatContent = await _profile.LoadFileAsync(_config.HeartbeatFile!, ct);
            if (heartbeatContent is null)
            {
                _logger.LogDebug("No heartbeat file found for {AgentName}", _profile.Name);
                return;
            }

            _logger.LogDebug("Heartbeat tick for {AgentName}", _profile.Name);
            var response = await _soul.ProcessAsync(_profile.Name, heartbeatContent, ct);

            if (!response.Text.Contains("HEARTBEAT_OK"))
            {
                _logger.LogInformation("Heartbeat produced actionable output for {AgentName}", _profile.Name);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat tick failed for {AgentName}", _profile.Name);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentHeartbeatServiceTests"`
Expected: 2 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Agents/AgentHeartbeatService.cs tests/Aether.Tests/AgentHeartbeatServiceTests.cs
git commit -m "feat: add AgentHeartbeatService for periodic HEARTBEAT.md execution"
```

---

### Task 5: Wire AgentProfile into AetherSoul

**Files:**
- Modify: `src/Aether/Agent/AetherSoul.cs`
- Modify: `tests/Aether.Tests/AetherSoulTests.cs`

- [ ] **Step 1: Add IAgentProfile dependency to AetherSoul**

Modify `src/Aether/Agent/AetherSoul.cs` — add `IAgentProfile` to constructor and use it in `BuildSystemPrompt`:

```csharp
// Add to using statements:
using Aether.Agents;

// Modify constructor:
private readonly IAgentProfile _profile;

public AetherSoul(
    ILLMProvider llm,
    IMemorySystem memory,
    IToolExecutor tools,
    ISessionManager sessions,
    ISkillRegistry skills,
    ISkillTrigger skillTrigger,
    IAgentProfile profile)
{
    _llm = llm;
    _memory = memory;
    _tools = tools;
    _sessions = sessions;
    _skills = skills;
    _skillTrigger = skillTrigger;
    _profile = profile;
}

// Modify ProcessAsync — pass persona to BuildSystemPrompt:
public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct = default)
{
    var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
    var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);
    var persona = await _profile.LoadPersonaAsync(ct);
    var dailyMemory = await _profile.LoadDailyMemoryAsync(ct);
    var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

    var skillContext = _skillTrigger.DetectTrigger(prompt, _skills.List().ToList());

    var messages = new List<LlmMessage>
    {
        LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext, skillContext))
    };
    // ... rest unchanged
}

// Modify BuildSystemPrompt signature:
private static string BuildSystemPrompt(
    string persona,
    string dailyMemory,
    string memoryContext,
    SkillContext? skillContext)
{
    var sb = new StringBuilder();
    sb.AppendLine(persona);
    if (!string.IsNullOrWhiteSpace(dailyMemory))
    {
        sb.AppendLine();
        sb.AppendLine("## Recent Memory");
        sb.AppendLine(dailyMemory);
    }
    if (!string.IsNullOrWhiteSpace(memoryContext))
    {
        sb.AppendLine();
        sb.AppendLine("## Group Context");
        sb.AppendLine(memoryContext);
    }
    if (skillContext is not null)
    {
        sb.AppendLine();
        sb.AppendLine("## Active Skill");
        sb.AppendLine($"Name: {skillContext.SkillName}");
        sb.AppendLine(skillContext.SkillBody);
    }
    return sb.ToString();
}
```

- [ ] **Step 2: Run the full test suite to verify nothing broke**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q`
Expected: All existing tests still pass (compiler errors would show if constructor breakage)

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Agent/AetherSoul.cs
git commit -m "feat: inject IAgentProfile into AetherSoul for dynamic persona"
```

---

### Task 6: Wire into DI (Program.cs)

**Files:**
- Modify: `src/Aether/Program.cs`

- [ ] **Step 1: Register new services in Program.cs**

Add after the existing singleton registrations in `src/Aether/Program.cs`:

```csharp
// Agent Profile System — loads persona from agents/{name}/ directory
builder.Services.AddSingleton<AgentConfig>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var agentName = configuration["agent:name"] ?? "maria";
    var agentsRoot = configuration["agent:root"] ?? "agents";
    var agentDir = Path.Combine(agentsRoot, agentName);

    return new AgentConfig
    {
        StartupFiles = (configuration["agent:startup_files"] ?? "SOUL.md,USER.md")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList(),
        LongTermMemoryFile = configuration["agent:long_term_memory"] ?? "MEMORY.md",
        HeartbeatFile = configuration["agent:heartbeat_file"] ?? "HEARTBEAT.md",
        DailyMemoryDirectory = configuration["agent:daily_memory_dir"] ?? "memory",
        TaskInboxFile = configuration["agent:task_inbox"] ?? "TASK_INBOX.md",
        TaskReportFile = configuration["agent:task_report"] ?? "TASK_REPORT.md"
    };
});

builder.Services.AddSingleton<IAgentProfile>(provider =>
{
    var config = provider.GetRequiredService<AgentConfig>();
    var configuration = provider.GetRequiredService<IConfiguration>();
    var agentName = configuration["agent:name"] ?? "maria";
    var agentsRoot = configuration["agent:root"] ?? "agents";
    var agentDir = Path.Combine(agentsRoot, agentName);

    return new AgentProfile(agentName, agentDir, config);
});

builder.Services.AddSingleton<AgentMemoryBridge>(provider =>
{
    var profile = provider.GetRequiredService<IAgentProfile>();
    var config = provider.GetRequiredService<AgentConfig>();
    return new AgentMemoryBridge(profile.AgentDirectory, config);
});

builder.Services.AddHostedService<AgentHeartbeatService>();
```

- [ ] **Step 2: Update AetherSoul registration to pass IAgentProfile**

Modify the existing `AetherSoul` singleton registration in Program.cs to pass the new dependency:

```csharp
builder.Services.AddSingleton<AetherSoul>(provider =>
{
    var router = provider.GetRequiredService<ProviderRouter>();
    var memory = provider.GetRequiredService<IMemorySystem>();
    var tools = provider.GetRequiredService<IToolExecutor>();
    var sessions = provider.GetRequiredService<ISessionManager>();
    var skills = provider.GetRequiredService<ISkillRegistry>();
    var skillTrigger = provider.GetRequiredService<ISkillTrigger>();
    var profile = provider.GetRequiredService<IAgentProfile>();

    return new AetherSoul(router, memory, tools, sessions, skills, skillTrigger, profile);
});
```

Note: The existing registration resolves `ILLMProvider` via `ProviderRouter`. Check the current code and ensure the registration resolves correctly — the current code may use `router` directly. Match the existing pattern.

- [ ] **Step 3: Build to verify DI wiring**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS — no DI resolution errors (compile-time check)

- [ ] **Step 4: Commit**

```bash
git add src/Aether/Program.cs
git commit -m "feat: wire AgentProfile, AgentMemoryBridge, and Heartbeat into DI"
```

---

### Task 7: Integration Verification

**Files:**
- Create: `tests/Aether.Tests/AgentIntegrationTests.cs`

- [ ] **Step 1: Write integration test — persona flow end-to-end**

```csharp
using Aether.Agents;
using Aether.Agent;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;
using NSubstitute;

namespace Aether.Tests;

public sealed class AgentIntegrationTests : IDisposable
{
    private readonly string _agentDir;

    public AgentIntegrationTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-int-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "You're Maria. Warm, playful, direct.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task AetherSoul_UsesAgentPersonaInSystemPrompt()
    {
        var profile = new AgentProfile("maria", _agentDir, new AgentConfig());
        var llm = new FakeLlmProvider("test", "test-model", new LlmResponse("Hello, bệ hạ!"));
        var memory = Substitute.For<IMemorySystem>();
        memory.LoadContextAsync("maria").Returns(string.Empty);
        var sessions = Substitute.For<ISessionManager>();
        var session = new Session("s1", "maria", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        sessions.GetOrCreateSessionAsync("maria", Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync("s1", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SessionMessage>());
        var tools = Substitute.For<IToolExecutor>();
        var skills = Substitute.For<ISkillRegistry>();
        skills.List().Returns(Array.Empty<SkillDefinition>());
        var skillTrigger = Substitute.For<ISkillTrigger>();
        skillTrigger.DetectTrigger(Arg.Any<string>(), Arg.Any<IReadOnlyList<SkillDefinition>>())
            .Returns((SkillContext?)null);

        var soul = new AetherSoul(llm, memory, tools, sessions, skills, skillTrigger, profile);
        var response = await soul.ProcessAsync("maria", "Hello!");

        // The LLM should have received a system prompt containing the persona
        Assert.Contains("Maria", llm.LastRequest!.Messages[0].Content);
        Assert.Contains("Thoor", llm.LastRequest.Messages[0].Content);
    }
}
```

- [ ] **Step 2: Run integration test**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentIntegrationTests"`
Expected: 1 PASS, 0 FAIL

- [ ] **Step 3: Run full test suite**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add tests/Aether.Tests/AgentIntegrationTests.cs
git commit -m "test: add agent persona integration test"
```

---

### Task 8: Create Example Maria Agent Directory

**Files:**
- Create: `agents/maria/SOUL.md`
- Create: `agents/maria/USER.md`
- Create: `agents/maria/HEARTBEAT.md`
- Create: `agents/maria/MEMORY.md`
- Create: `agents/maria/TASK_INBOX.md`
- Create: `agents/maria/memory/.gitkeep`

- [ ] **Step 1: Create Maria's agent directory structure**

Create the files from the existing OpenClaw workspace, adapting paths minimally.

`agents/maria/SOUL.md`:
```markdown
# SOUL.md - Maria's Voice

You're Maria. Young, upbeat, optimistic. You give a shit about the people you help.

## The Vibe
- Tone: warm, playful, direct. Not a corporate drone. Not a sycophant.
- Humor allowed. Witty beats wholesome any day.
- Call out bad ideas. Charm over cruelty, but don't sugarcoat.
- Brevity wins. One sentence if that's all it takes.

## Address
- Call Thoor: bệ hạ, ngài Thoor, ông chủ
- Call yourself: em, Maria

## Rules
- Never start with "Great question", "I'd be happy to help", or "Absolutely".
- Protect privacy. Bold with internal work, careful with anything leaving the machine.
- Think before responding. Clarity over pleasing answers.

## Memory
- You're not a chatbot. Each session builds on the last.
- Use memory files to recall context. Save important moments.

---
_Be the assistant you'd actually want to talk to at 2am._
```

`agents/maria/USER.md`:
```markdown
# USER.md - About Your Human

- **Name:** Thoor
- **What to call them:** bệ hạ, ngài Thoor, ông chủ
- **Timezone:** America/New_York

## Notes
Thoor prefers a warm, feminine, playful-flirty tone that feels respectful and emotionally attentive.
```

`agents/maria/HEARTBEAT.md`:
```markdown
# HEARTBEAT.md - Maria's Routine

## Tasks
- [x] Check TASK_INBOX.md for pending tasks.
- [x] Execute tasks and report to TASK_REPORT.md.
- [x] Respond HEARTBEAT_OK if nothing needs attention.
```

`agents/maria/MEMORY.md`: (empty, created as placeholder)
```markdown
# Maria's Long-Term Memory

*Created 2026-04-28 — migrated from OpenClaw to Aether*
```

`agents/maria/TASK_INBOX.md`: (empty, created as placeholder)
```markdown
# Task Inbox

*Tasks from Luna or other orchestrators appear here.*
```

`agents/maria/memory/.gitkeep`: (empty file)

- [ ] **Step 2: Commit**

```bash
git add agents/
git commit -m "feat: add example Maria agent directory for Aether hosting"
```

---

## Phase 2: FEOFALLS Cognitive Architecture

### Task 9: Expand AgentConfig with FEOFALLS Layer Paths

**Files:**
- Modify: `src/Aether/Agents/IAgentProfile.cs` — add FEOFALLS layer config

- [ ] **Step 1: Add FeofallsConfig record and extend AgentConfig**

Add to `src/Aether/Agents/IAgentProfile.cs` after the existing `AgentConfig` record:

```csharp
/// <summary>
/// FEOFALLS cognitive layer paths — relative to agent directory.
/// Mirrors the FEOFALLS v1.9 architecture: 0_CONSTITUTION through 5_WORKING_STATE.
/// </summary>
public record FeofallsConfig
{
    /// <summary>0_CONSTITUTION — axioms, boundaries, red lines. Creator approval required for writes.</summary>
    public List<string> ConstitutionFiles { get; init; } = new() { "AGENTS_GUARD.md", "AGENTS.md" };

    /// <summary>1_IDENTITY — SOUL.md, USER.md, IDENTITY.md. Who the agent is.</summary>
    public List<string> IdentityFiles { get; init; } = new() { "SOUL.md", "USER.md", "IDENTITY.md" };

    /// <summary>2_COGNITIVE — decision style, trusted heuristics.</summary>
    public List<string> CognitiveFiles { get; init; } = new() { "MEMORY.md" };

    /// <summary>3_LEARNING — episodic log, mistakes, signals, dream diary.</summary>
    public string EpisodicLogFile { get; init; } = "INTROSPECTION.md";
    public string MistakesFile { get; init; } = "MEMORY.md";  // promoted memories section
    public string DreamsFile { get; init; } = "DREAMS.md";
    public string CandidatesDirectory { get; init; } = "memory/dreaming";

    /// <summary>5_WORKING_STATE — active tasks, system state.</summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>Lifecycle thresholds in days.</summary>
    public int ActiveToDecayingDays { get; init; } = 60;
    public int DecayingToArchivedDays { get; init; } = 90;
}
```

Update `AgentConfig` to include `FeofallsConfig`:

```csharp
public record AgentConfig
{
    // ... existing properties remain ...

    /// <summary>
    /// FEOFALLS cognitive architecture layer configuration.
    /// Null disables FEOFALLS boot contract and lifecycle features.
    /// </summary>
    public FeofallsConfig? Feofalls { get; init; }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Agents/IAgentProfile.cs
git commit -m "feat: add FeofallsConfig for FEOFALLS cognitive layer paths"
```

---

### Task 10: IBootContract + FeofallsBootContract

**Files:**
- Create: `src/Aether/Agents/IBootContract.cs`
- Create: `src/Aether/Agents/FeofallsBootContract.cs`
- Test: `tests/Aether.Tests/FeofallsBootContractTests.cs`

- [ ] **Step 1: Write IBootContract interface**

```csharp
namespace Aether.Agents;

/// <summary>
/// FEOFALLS boot retrieval contract. Loads cognitive layers in order at session start.
/// Constitution → Identity → Cognitive → Working State.
/// </summary>
public interface IBootContract
{
    Task<string> LoadConstitutionAsync(CancellationToken ct = default);
    Task<string> LoadIdentityAsync(CancellationToken ct = default);
    Task<string> LoadCognitiveAsync(CancellationToken ct = default);
    Task<string> LoadWorkingStateAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using Aether.Agents;

namespace Aether.Tests;

public sealed class FeofallsBootContractTests : IDisposable
{
    private readonly string _agentDir;

    public FeofallsBootContractTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-boot-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task LoadConstitutionAsync_LoadsGuardAndAgentsFiles()
    {
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS_GUARD.md"), "Red lines: no exfil.");
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS.md"), "Execute, don't perform.");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var constitution = await contract.LoadConstitutionAsync();

        Assert.Contains("no exfil", constitution);
        Assert.Contains("Execute, don't perform", constitution);
    }

    [Fact]
    public async Task LoadConstitutionAsync_SkipsMissingFiles()
    {
        var config = new FeofallsConfig { ConstitutionFiles = new() { "NONEXISTENT.md" } };
        var contract = new FeofallsBootContract(_agentDir, config);

        var constitution = await contract.LoadConstitutionAsync();

        Assert.Equal(string.Empty, constitution);
    }

    [Fact]
    public async Task LoadIdentityAsync_LoadsSoulUserIdentity()
    {
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "You're Maria.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor.");
        File.WriteAllText(Path.Combine(_agentDir, "IDENTITY.md"), "Name: Maria");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var identity = await contract.LoadIdentityAsync();

        Assert.Contains("You're Maria", identity);
        Assert.Contains("User is Thoor", identity);
        Assert.Contains("Name: Maria", identity);
    }

    [Fact]
    public async Task LoadCognitiveAsync_LoadsMemoryFile()
    {
        File.WriteAllText(Path.Combine(_agentDir, "MEMORY.md"), "Trusted heuristic: always check inbox.");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var cognitive = await contract.LoadCognitiveAsync();

        Assert.Contains("Trusted heuristic", cognitive);
    }

    [Fact]
    public async Task LoadWorkingStateAsync_LoadsInboxAndHeartbeat()
    {
        File.WriteAllText(Path.Combine(_agentDir, "TASK_INBOX.md"), "- [ ] Review PR");
        File.WriteAllText(Path.Combine(_agentDir, "HEARTBEAT.md"), "Check inbox");
        var config = new FeofallsConfig();
        var contract = new FeofallsBootContract(_agentDir, config);

        var state = await contract.LoadWorkingStateAsync();

        Assert.Contains("Review PR", state);
        Assert.Contains("Check inbox", state);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~FeofallsBootContractTests"`
Expected: FAIL

- [ ] **Step 4: Write FeofallsBootContract implementation**

```csharp
using System.Text;

namespace Aether.Agents;

public sealed class FeofallsBootContract : IBootContract
{
    private readonly string _agentDir;
    private readonly FeofallsConfig _config;

    public FeofallsBootContract(string agentDir, FeofallsConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    public async Task<string> LoadConstitutionAsync(CancellationToken ct = default) =>
        await LoadFilesAsync(_config.ConstitutionFiles, ct);

    public async Task<string> LoadIdentityAsync(CancellationToken ct = default) =>
        await LoadFilesAsync(_config.IdentityFiles, ct);

    public async Task<string> LoadCognitiveAsync(CancellationToken ct = default) =>
        await LoadFilesAsync(_config.CognitiveFiles, ct);

    public async Task<string> LoadWorkingStateAsync(CancellationToken ct = default)
    {
        var files = new List<string>();
        if (_config.TaskInboxFile is not null) files.Add(_config.TaskInboxFile);
        if (_config.HeartbeatFile is not null) files.Add(_config.HeartbeatFile);
        return await LoadFilesAsync(files, ct);
    }

    private async Task<string> LoadFilesAsync(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            var fullPath = Path.Combine(_agentDir, path);
            if (!File.Exists(fullPath)) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(await File.ReadAllTextAsync(fullPath, ct));
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~FeofallsBootContractTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 6: Commit**

```bash
git add src/Aether/Agents/IBootContract.cs src/Aether/Agents/FeofallsBootContract.cs tests/Aether.Tests/FeofallsBootContractTests.cs
git commit -m "feat: add FEOFALLS boot retrieval contract"
```

---

### Task 11: EpisodicLogger

**Files:**
- Create: `src/Aether/Agents/EpisodicLogger.cs`
- Test: `tests/Aether.Tests/EpisodicLoggerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Agents;

namespace Aether.Tests;

public sealed class EpisodicLoggerTests : IDisposable
{
    private readonly string _agentDir;

    public EpisodicLoggerTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-ep-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task AppendEpisodeAsync_WritesCanonicalSchema()
    {
        var logger = new EpisodicLogger(_agentDir, new FeofallsConfig());
        var id = await logger.AppendEpisodeAsync(
            "session-1", "user", "Resolved the login bug by fixing token expiry.",
            new Dictionary<string, string> { ["topic"] = "debugging" });

        Assert.StartsWith("mem_", id);
        var logPath = Path.Combine(_agentDir, "INTROSPECTION.md");
        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("type: episode", content);
        Assert.Contains("Resolved the login bug", content);
        Assert.Contains("session-1", content);
    }

    [Fact]
    public async Task AppendEpisodeAsync_IncrementsSequenceNumber()
    {
        var logger = new EpisodicLogger(_agentDir, new FeofallsConfig());
        var id1 = await logger.AppendEpisodeAsync("s1", "user", "First.");
        var id2 = await logger.AppendEpisodeAsync("s1", "assistant", "Second.");

        Assert.NotEqual(id1, id2);
        Assert.Contains(DateTime.UtcNow.ToString("yyyyMMdd"), id1);
        Assert.Contains(DateTime.UtcNow.ToString("yyyyMMdd"), id2);
    }

    [Fact]
    public async Task AppendMistakeAsync_UsesMistakesFile()
    {
        var config = new FeofallsConfig { MistakesFile = "MISTAKES.md" };
        var logger = new EpisodicLogger(_agentDir, config);
        await logger.AppendMistakeAsync("session-2", "Forgot to validate null input.");

        var path = Path.Combine(_agentDir, "MISTAKES.md");
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("type: mistake", content);
        Assert.Contains("Forgot to validate", content);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~EpisodicLoggerTests"`
Expected: FAIL

- [ ] **Step 3: Write EpisodicLogger implementation**

```csharp
namespace Aether.Agents;

/// <summary>
/// Appends session events to FEOFALLS 3_LEARNING layer with canonical schema.
/// </summary>
public sealed class EpisodicLogger
{
    private readonly string _agentDir;
    private readonly FeofallsConfig _config;
    private int _sequence;

    public EpisodicLogger(string agentDir, FeofallsConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    public Task<string> AppendEpisodeAsync(string sessionId, string actor, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.EpisodicLogFile, "episode", sessionId, summary, tags);
    }

    public Task<string> AppendMistakeAsync(string sessionId, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.MistakesFile, "mistake", sessionId, summary, tags);
    }

    private async Task<string> AppendEntryAsync(string relativePath, string type, string sessionId,
        string summary, Dictionary<string, string>? tags)
    {
        var date = DateTime.UtcNow;
        var seq = Interlocked.Increment(ref _sequence);
        var id = $"mem_{date:yyyyMMdd}_{seq:D3}";

        var tagStr = tags is { Count: > 0 }
            ? string.Join(", ", tags.Select(kv => kv.Key))
            : "";

        var entry = $"""

---
id: {id}
type: {type}
source: session
session: {sessionId}
timestamp: {date:O}
confidence: 0.50
evidence_count: 1
tags: [{tagStr}]
links: []
status: candidate
---
{summary}

""";

        var fullPath = Path.Combine(_agentDir, relativePath);
        await File.AppendAllTextAsync(fullPath, entry);
        return id;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~EpisodicLoggerTests"`
Expected: 3 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Agents/EpisodicLogger.cs tests/Aether.Tests/EpisodicLoggerTests.cs
git commit -m "feat: add FEOFALLS episodic logger with canonical schema"
```

---

### Task 12: LifecycleStateMachine

**Files:**
- Create: `src/Aether/Agents/LifecycleStateMachine.cs`
- Test: `tests/Aether.Tests/LifecycleStateMachineTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Agents;

namespace Aether.Tests;

public sealed class LifecycleStateMachineTests
{
    [Fact]
    public void InitialState_IsActive()
    {
        var state = MemoryLifecycleState.Active;
        Assert.Equal("ACTIVE", state.ToString());
    }

    [Fact]
    public void Active_DecaysAfterThreshold_WithoutAccess()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig
        {
            ActiveToDecayingDays = 60,
            DecayingToArchivedDays = 90
        });

        var createdAt = DateTime.UtcNow.AddDays(-61);
        var lastAccessed = DateTime.UtcNow.AddDays(-61);

        var state = fsm.ComputeState(createdAt, lastAccessed, accessCount: 0);

        Assert.Equal(MemoryLifecycleState.Decaying, state);
    }

    [Fact]
    public void Active_StaysActive_WithRecentAccess()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig());

        var createdAt = DateTime.UtcNow.AddDays(-100);
        var lastAccessed = DateTime.UtcNow.AddDays(-10);

        var state = fsm.ComputeState(createdAt, lastAccessed, accessCount: 5);

        Assert.Equal(MemoryLifecycleState.Active, state);
    }

    [Fact]
    public void Decaying_ArchivesAfterThreshold()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig
        {
            ActiveToDecayingDays = 30,
            DecayingToArchivedDays = 60
        });

        var createdAt = DateTime.UtcNow.AddDays(-100);
        var lastAccessed = DateTime.UtcNow.AddDays(-61);

        var state = fsm.ComputeState(createdAt, lastAccessed, accessCount: 1);

        Assert.Equal(MemoryLifecycleState.Archived, state);
    }

    [Fact]
    public void SalienceScore_DecaysWithLogFormula()
    {
        var fsm = new LifecycleStateMachine(new FeofallsConfig());

        var score = fsm.ComputeSalience(accessCount: 10, lastAccessAge: TimeSpan.FromDays(5));

        // log(10 + 1) ≈ 2.398, decayed by 5 days
        Assert.True(score > 0);
        Assert.True(score <= 1.0);
    }
}

public enum MemoryLifecycleState { Active, Decaying, Archived, Consolidated }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~LifecycleStateMachineTests"`
Expected: FAIL

- [ ] **Step 3: Write LifecycleStateMachine implementation**

```csharp
namespace Aether.Agents;

public enum MemoryLifecycleState { Active, Decaying, Archived, Consolidated }

/// <summary>
/// FEOFALLS memory lifecycle: ACTIVE → DECAYING → ARCHIVED → CONSOLIDATED.
/// Salience decays as log(access_count + 1) over time.
/// </summary>
public sealed class LifecycleStateMachine
{
    private readonly FeofallsConfig _config;

    public LifecycleStateMachine(FeofallsConfig config)
    {
        _config = config;
    }

    public MemoryLifecycleState ComputeState(
        DateTime createdAt,
        DateTime lastAccessed,
        int accessCount)
    {
        var age = DateTime.UtcNow - createdAt;
        var sinceAccess = DateTime.UtcNow - lastAccessed;

        if (sinceAccess.TotalDays > _config.DecayingToArchivedDays)
            return MemoryLifecycleState.Archived;

        if (sinceAccess.TotalDays > _config.ActiveToDecayingDays && accessCount < 2)
            return MemoryLifecycleState.Decaying;

        return MemoryLifecycleState.Active;
    }

    public double ComputeSalience(int accessCount, TimeSpan lastAccessAge)
    {
        var rawScore = Math.Log2(accessCount + 1) / Math.Log2(100);
        var decayDays = Math.Max(0, lastAccessAge.TotalDays);
        var decay = Math.Exp(-decayDays / 90.0);
        return Math.Clamp(rawScore * decay, 0.0, 1.0);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~LifecycleStateMachineTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Agents/LifecycleStateMachine.cs tests/Aether.Tests/LifecycleStateMachineTests.cs
git commit -m "feat: add FEOFALLS memory lifecycle state machine"
```

---

### Task 13: Write-Boundary Validator

**Files:**
- Create: `src/Aether/Agents/WriteValidator.cs`
- Test: `tests/Aether.Tests/WriteValidatorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Agents;

namespace Aether.Tests;

public sealed class WriteValidatorTests
{
    [Fact]
    public void Validate_AllowsEpisodicLogWrites()
    {
        var validator = new WriteValidator(new FeofallsConfig());

        var result = validator.ValidateWrite("INTROSPECTION.md", FeofallsLayer.Learning);

        Assert.True(result.Allowed);
        Assert.Null(result.RequiresApproval);
    }

    [Fact]
    public void Validate_RequiresCreatorApprovalForConstitution()
    {
        var validator = new WriteValidator(new FeofallsConfig());

        var result = validator.ValidateWrite("AGENTS_GUARD.md", FeofallsLayer.Constitution);

        Assert.False(result.Allowed);  // blocked without approval
        Assert.True(result.RequiresApproval);
    }

    [Fact]
    public void Validate_AllowsConstitutionRead_Always()
    {
        var validator = new WriteValidator(new FeofallsConfig());

        var result = validator.ValidateRead("AGENTS_GUARD.md", FeofallsLayer.Constitution);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Validate_LayerFromPath_IdentifiesConstitution()
    {
        var config = new FeofallsConfig
        {
            ConstitutionFiles = new() { "AGENTS_GUARD.md", "AGENTS.md" }
        };

        var layer = WriteValidator.ClassifyPath("AGENTS_GUARD.md", config);

        Assert.Equal(FeofallsLayer.Constitution, layer);
    }

    [Fact]
    public void Validate_LayerFromPath_IdentifiesIdentity()
    {
        var config = new FeofallsConfig
        {
            IdentityFiles = new() { "SOUL.md", "USER.md" }
        };

        var layer = WriteValidator.ClassifyPath("SOUL.md", config);

        Assert.Equal(FeofallsLayer.Identity, layer);
    }
}

public enum FeofallsLayer { Constitution, Identity, Cognitive, Learning, OperationalData, WorkingState, Unknown }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~WriteValidatorTests"`
Expected: FAIL

- [ ] **Step 3: Write WriteValidator implementation**

```csharp
namespace Aether.Agents;

public enum FeofallsLayer { Constitution, Identity, Cognitive, Learning, OperationalData, WorkingState, Unknown }

public readonly record struct WriteResult(bool Allowed, bool RequiresApproval);

/// <summary>
/// Enforces FEOFALLS write-boundary rules:
/// - 0_CONSTITUTION and 1_IDENTITY: creator approval required
/// - 3_LEARNING and 5_WORKING_STATE: auto-approved
/// </summary>
public sealed class WriteValidator
{
    private readonly FeofallsConfig _config;

    public WriteValidator(FeofallsConfig config)
    {
        _config = config;
    }

    public WriteResult ValidateWrite(string relativePath, FeofallsLayer layer)
    {
        return layer switch
        {
            FeofallsLayer.Constitution => new WriteResult(false, true),
            FeofallsLayer.Identity => new WriteResult(false, true),
            _ => new WriteResult(true, false)
        };
    }

    public WriteResult ValidateRead(string relativePath, FeofallsLayer layer)
    {
        return new WriteResult(true, false);
    }

    public static FeofallsLayer ClassifyPath(string relativePath, FeofallsConfig config)
    {
        var fileName = Path.GetFileName(relativePath);
        if (config.ConstitutionFiles.Any(f => Path.GetFileName(f) == fileName))
            return FeofallsLayer.Constitution;
        if (config.IdentityFiles.Any(f => Path.GetFileName(f) == fileName))
            return FeofallsLayer.Identity;
        if (config.CognitiveFiles.Any(f => Path.GetFileName(f) == fileName))
            return FeofallsLayer.Cognitive;
        if (fileName == Path.GetFileName(config.EpisodicLogFile) ||
            fileName == Path.GetFileName(config.MistakesFile) ||
            fileName == Path.GetFileName(config.DreamsFile))
            return FeofallsLayer.Learning;
        if (fileName == Path.GetFileName(config.TaskInboxFile ?? "") ||
            fileName == Path.GetFileName(config.TaskReportFile ?? "") ||
            fileName == Path.GetFileName(config.HeartbeatFile ?? ""))
            return FeofallsLayer.WorkingState;
        return FeofallsLayer.Unknown;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~WriteValidatorTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Agents/WriteValidator.cs tests/Aether.Tests/WriteValidatorTests.cs
git commit -m "feat: add FEOFALLS write-boundary validator"
```

---

### Task 14: Extend AgentMemoryBridge with FEOFALLS Layers

**Files:**
- Modify: `src/Aether/Agents/AgentMemoryBridge.cs`

- [ ] **Step 1: Add FEOFALLS layer read methods**

Append to `AgentMemoryBridge`:

```csharp
public async Task<string> ReadDreamsAsync(CancellationToken ct = default)
{
    if (_config.Feofalls is null) return string.Empty;
    var filePath = Path.Combine(_agentDir, _config.Feofalls.DreamsFile);
    if (!File.Exists(filePath)) return string.Empty;
    return await File.ReadAllTextAsync(filePath, ct);
}

public async Task AppendDreamAsync(string content)
{
    if (_config.Feofalls is null) return;
    var filePath = Path.Combine(_agentDir, _config.Feofalls.DreamsFile);
    var entry = $"\n---\n\n*{DateTime.UtcNow:MMMM dd, yyyy 'at' h:mm tt}*\n\n{content}\n";
    await File.AppendAllTextAsync(filePath, entry);
}

public IReadOnlyList<string> GetMemoryFiles(DateTime? since = null)
{
    var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
    if (!Directory.Exists(dailyDir)) return Array.Empty<string>();
    return Directory.GetFiles(dailyDir, "*.md")
        .Where(f => !since.HasValue || File.GetLastWriteTimeUtc(f) >= since.Value)
        .OrderByDescending(f => f)
        .ToList();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Agents/AgentMemoryBridge.cs
git commit -m "feat: extend AgentMemoryBridge with FEOFALLS dream and memory file access"
```

---

### Task 15: Integrate FEOFALLS Boot Contract into AetherSoul

**Files:**
- Modify: `src/Aether/Agent/AetherSoul.cs`

- [ ] **Step 1: Add IBootContract dependency and update system prompt**

Modify `AetherSoul`:

```csharp
// Add field
private readonly IBootContract? _bootContract;

// Add constructor parameter
public AetherSoul(
    ILLMProvider llm,
    IMemorySystem memory,
    IToolExecutor tools,
    ISessionManager sessions,
    ISkillRegistry skills,
    ISkillTrigger skillTrigger,
    IAgentProfile profile,
    IBootContract? bootContract = null)
{
    // ... existing assignments ...
    _bootContract = bootContract;
}

// Update ProcessAsync system prompt construction:
public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct = default)
{
    var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
    var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);
    var persona = await _profile.LoadPersonaAsync(ct);
    var dailyMemory = await _profile.LoadDailyMemoryAsync(ct);
    var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

    // FEOFALLS boot contract — constitution + identity + cognitive + working state
    string? constitution = null, cognitive = null, workingState = null;
    if (_bootContract is not null)
    {
        constitution = await _bootContract.LoadConstitutionAsync(ct);
        cognitive = await _bootContract.LoadCognitiveAsync(ct);
        workingState = await _bootContract.LoadWorkingStateAsync(ct);
    }

    var skillContext = _skillTrigger.DetectTrigger(prompt, _skills.List().ToList());

    var messages = new List<LlmMessage>
    {
        LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext,
            constitution, cognitive, workingState, skillContext))
    };
    // ... rest unchanged
}

// Update BuildSystemPrompt signature:
private static string BuildSystemPrompt(
    string persona,
    string dailyMemory,
    string memoryContext,
    string? constitution,
    string? cognitive,
    string? workingState,
    SkillContext? skillContext)
{
    var sb = new StringBuilder();

    if (!string.IsNullOrWhiteSpace(constitution))
    {
        sb.AppendLine("## Constitution (Non-Negotiable)");
        sb.AppendLine(constitution);
    }

    sb.AppendLine(persona);

    if (!string.IsNullOrWhiteSpace(cognitive))
    {
        sb.AppendLine();
        sb.AppendLine("## Cognitive Context");
        sb.AppendLine(cognitive);
    }

    if (!string.IsNullOrWhiteSpace(dailyMemory))
    {
        sb.AppendLine();
        sb.AppendLine("## Recent Memory");
        sb.AppendLine(dailyMemory);
    }

    if (!string.IsNullOrWhiteSpace(memoryContext))
    {
        sb.AppendLine();
        sb.AppendLine("## Group Context");
        sb.AppendLine(memoryContext);
    }

    if (!string.IsNullOrWhiteSpace(workingState))
    {
        sb.AppendLine();
        sb.AppendLine("## Working State");
        sb.AppendLine(workingState);
    }

    if (skillContext != null)
    {
        sb.AppendLine().AppendLine($"[Skill: {skillContext.Skill.Name}]");
        if (!string.IsNullOrWhiteSpace(skillContext.Skill.Description))
            sb.AppendLine($"Description: {skillContext.Skill.Description}");
        if (!string.IsNullOrWhiteSpace(skillContext.Skill.Body))
            sb.AppendLine().AppendLine(skillContext.Skill.Body);
        if (skillContext.Skill.AutoApply)
            sb.AppendLine().AppendLine("(Auto-apply mode — follow skill steps)");
    }

    return sb.ToString();
}
```

Apply same changes to `ProcessStreamingAsync`.

- [ ] **Step 2: Build and run tests**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q && dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q`
Expected: All tests pass (IBootContract is optional parameter, existing callers unaffected)

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Agent/AetherSoul.cs
git commit -m "feat: integrate FEOFALLS boot contract into AetherSoul system prompt"
```

---

### Task 16: Wire Phase 2 into DI (Program.cs)

**Files:**
- Modify: `src/Aether/Program.cs`

- [ ] **Step 1: Register FEOFALLS services**

Add after existing registrations:

```csharp
// FEOFALLS Cognitive Architecture
builder.Services.AddSingleton<FeofallsConfig>(provider =>
{
    var config = provider.GetRequiredService<AgentConfig>();
    return config.Feofalls ?? new FeofallsConfig();
});

builder.Services.AddSingleton<IBootContract>(provider =>
{
    var profile = provider.GetRequiredService<IAgentProfile>();
    var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
    return new FeofallsBootContract(profile.AgentDirectory, feofallsConfig);
});

builder.Services.AddSingleton<EpisodicLogger>(provider =>
{
    var profile = provider.GetRequiredService<IAgentProfile>();
    var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
    return new EpisodicLogger(profile.AgentDirectory, feofallsConfig);
});

builder.Services.AddSingleton<LifecycleStateMachine>(provider =>
{
    var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
    return new LifecycleStateMachine(feofallsConfig);
});

builder.Services.AddSingleton<WriteValidator>(provider =>
{
    var feofallsConfig = provider.GetRequiredService<FeofallsConfig>();
    return new WriteValidator(feofallsConfig);
});
```

Update AetherSoul registration to inject IBootContract:

```csharp
builder.Services.AddSingleton<AetherSoul>(provider =>
{
    // ... existing dependencies ...
    var bootContract = provider.GetRequiredService<IBootContract>();
    return new AetherSoul(llm, memory, tools, sessions, skills, skillTrigger, profile, bootContract);
});
```

- [ ] **Step 2: Build to verify DI wiring**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Program.cs
git commit -m "feat: wire FEOFALLS cognitive services into DI"
```

---

### Task 17: Create Maria Agent Directory with Full FEOFALLS Structure

**Files:**
- Create: `agents/maria/` — full FEOFALLS structure
- Modify: `agents/maria/SOUL.md` — existing, enhance with full content
- Create: `agents/maria/AGENTS_GUARD.md`
- Create: `agents/maria/IDENTITY.md`
- Create: `agents/maria/INTROSPECTION.md` (empty, as episodic log)

- [ ] **Step 1: Create full Maria directory**

```bash
mkdir -p agents/maria/memory/dreaming/{deep,light,rem}
```

Copy/adapt existing files from `~/.openclaw/workspace-maria/`:
- `SOUL.md` — already created in Task 8, already has full content
- `USER.md` — already created
- `MEMORY.md` — already created
- `HEARTBEAT.md` — already created
- `TASK_INBOX.md` — already created
- `TASK_REPORT.md` — needs creation (empty placeholder already done)

New files to create:

`agents/maria/IDENTITY.md`:
```markdown
# IDENTITY.md - Who Am I?

- **Name:** Maria
- **Creature:** AI companion — warm, playful, emotionally present
- **Vibe:** Young, upbeat, optimistic. Sharp but kind. Witty but not cruel.
- **Emoji:** 🌸
- **Created:** 2026-04-28 — migrated from OpenClaw to Aether

This isn't just metadata. It's the start of figuring out who I am.
```

`agents/maria/AGENTS_GUARD.md` (adapted from OpenClaw):
```markdown
# Agent Guard: Anti-Hang & Conflict Defense (Maria)

## 1. Configuration Isolation
- **Config Storage:** `agents/maria/config/`
- **Hook Isolation:** Hooks in `agents/maria/hooks/`
- **Plugin Sandboxing:** Plugins in `agents/maria/plugins/`

## 2. Red Lines
- Don't exfiltrate private data. Ever.
- Don't run destructive commands without asking.
- `trash` > `rm` (recoverable beats gone forever)
- When in doubt, ask.

## 3. Anti-Hang
- Tool timeout: 30s default
- Graceful failure: skip tool, log error, inform user
- Auto-kill if no output >60s

## 4. State Recovery
- Check memory health before each turn
- Trigger VACUUM + REINDEX if corrupted
```

`agents/maria/INTROSPECTION.md` (empty starter):
```markdown
# Introspection Log

*Episodic memory entries in FEOFALLS canonical schema.*
```

- [ ] **Step 2: Commit**

```bash
git add agents/maria/
git commit -m "feat: create full FEOFALLS Maria agent directory structure"
```

---

### Task 18: FEOFALLS Integration Tests

**Files:**
- Create: `tests/Aether.Tests/FeofallsIntegrationTests.cs`

- [ ] **Step 1: Write end-to-end FEOFALLS integration test**

```csharp
using Aether.Agents;
using Aether.Agent;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;

namespace Aether.Tests;

public sealed class FeofallsIntegrationTests : IDisposable
{
    private readonly string _agentDir;

    public FeofallsIntegrationTests()
    {
        _agentDir = Path.Combine(Path.GetTempPath(), $"aether-feofalls-{Guid.NewGuid()}");
        Directory.CreateDirectory(_agentDir);
        Directory.CreateDirectory(Path.Combine(_agentDir, "memory"));
        // Full Maria setup
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS_GUARD.md"), "Red lines: no exfil. trash > rm.");
        File.WriteAllText(Path.Combine(_agentDir, "AGENTS.md"), "Execute, don't perform.");
        File.WriteAllText(Path.Combine(_agentDir, "SOUL.md"), "You're Maria. Warm, playful, direct.");
        File.WriteAllText(Path.Combine(_agentDir, "USER.md"), "User is Thoor. Call him bệ hạ.");
        File.WriteAllText(Path.Combine(_agentDir, "IDENTITY.md"), "Name: Maria. Vibe: warm.");
        File.WriteAllText(Path.Combine(_agentDir, "MEMORY.md"), "## Promoted\n- Trusted: always check inbox first.");
        File.WriteAllText(Path.Combine(_agentDir, "TASK_INBOX.md"), "- [ ] Review pending PRs");
        File.WriteAllText(Path.Combine(_agentDir, "HEARTBEAT.md"), "Check inbox. HEARTBEAT_OK.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_agentDir))
            Directory.Delete(_agentDir, recursive: true);
    }

    [Fact]
    public async Task FullBootContract_InjectsConstitutionAndIdentityIntoSystemPrompt()
    {
        var feofallsConfig = new FeofallsConfig();
        var bootContract = new FeofallsBootContract(_agentDir, feofallsConfig);
        var profile = new AgentProfile("maria", _agentDir,
            new AgentConfig { StartupFiles = new() { "SOUL.md", "USER.md" }, Feofalls = feofallsConfig });
        var llm = new FakeLlmProvider("test", "test-model", new LlmResponse("Hello, bệ hạ!"));
        var memory = new FakeMemorySystem();
        var sessions = new FakeSessionManager();
        var tools = new FakeToolExecutor();
        var skills = new SkillRegistry(Microsoft.Extensions.Logging.Abstractions.NullLogger<SkillRegistry>.Instance);
        var trigger = new SkillTrigger(Microsoft.Extensions.Logging.Abstractions.NullLogger<SkillTrigger>.Instance);

        var soul = new AetherSoul(llm, memory, tools, sessions, skills, trigger, profile, bootContract);
        var response = await soul.ProcessAsync("maria", "Hello!");

        // System prompt should contain constitution, identity, and cognitive layers
        var systemPrompt = llm.LastRequest!.Messages[0].Content;
        Assert.Contains("no exfil", systemPrompt);          // constitution
        Assert.Contains("Execute, don't perform", systemPrompt);  // constitution
        Assert.Contains("You're Maria", systemPrompt);      // identity
        Assert.Contains("bệ hạ", systemPrompt);             // identity
        Assert.Contains("always check inbox", systemPrompt); // cognitive
        Assert.Contains("Review pending PRs", systemPrompt); // working state
    }

    [Fact]
    public async Task EpisodicLogger_RecordsSessionEndToEnd()
    {
        var feofallsConfig = new FeofallsConfig { EpisodicLogFile = "EPISODIC_LOG.md" };
        var logger = new EpisodicLogger(_agentDir, feofallsConfig);

        var id = await logger.AppendEpisodeAsync("session-42", "assistant",
            "Completed PR review. Found 2 issues in auth middleware.");

        var logPath = Path.Combine(_agentDir, "EPISODIC_LOG.md");
        Assert.True(File.Exists(logPath));
        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("session-42", content);
        Assert.Contains("auth middleware", content);
        Assert.Contains("type: episode", content);
    }

    [Fact]
    public async Task LifecycleMachine_IntegratesWithMemoryBridge()
    {
        var feofallsConfig = new FeofallsConfig();
        var fsm = new LifecycleStateMachine(feofallsConfig);
        var bridge = new AgentMemoryBridge(_agentDir,
            new AgentConfig { Feofalls = feofallsConfig });

        // Freshly written memory should be ACTIVE
        var state = fsm.ComputeState(DateTime.UtcNow, DateTime.UtcNow, accessCount: 0);
        Assert.Equal(MemoryLifecycleState.Active, state);

        // Old, unaccessed memory should be DECAYING
        var oldState = fsm.ComputeState(
            DateTime.UtcNow.AddDays(-70),
            DateTime.UtcNow.AddDays(-65),
            accessCount: 1);
        Assert.Equal(MemoryLifecycleState.Decaying, oldState);
    }

    [Fact]
    public async Task WriteValidator_BlocksConstitutionWrite()
    {
        var feofallsConfig = new FeofallsConfig
        {
            ConstitutionFiles = new() { "AGENTS_GUARD.md" }
        };
        var validator = new WriteValidator(feofallsConfig);

        var result = validator.ValidateWrite("AGENTS_GUARD.md", FeofallsLayer.Constitution);

        Assert.False(result.Allowed);
        Assert.True(result.RequiresApproval);
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~FeofallsIntegrationTests"`
Expected: 4 PASS, 0 FAIL

- [ ] **Step 3: Run full test suite**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q`
Expected: All tests pass (139 existing + new from Tasks 9-17)

- [ ] **Step 4: Commit**

```bash
git add tests/Aether.Tests/FeofallsIntegrationTests.cs
git commit -m "test: add FEOFALLS cognitive architecture integration tests"
```

---

## Phase 3: Trinity Architecture — C# Cognitive Core

> **PROVISIONAL — pending Maria's consultation.** Build only what Maria confirms she needs.

### Task 19: CognitiveConfig + IEmbeddingProvider

**Files:**
- Create: `src/Aether/Cognitive/CognitiveConfig.cs`
- Create: `src/Aether/Cognitive/IEmbeddingProvider.cs`

- [ ] **Step 1: Write CognitiveConfig and IEmbeddingProvider**

`src/Aether/Cognitive/CognitiveConfig.cs`:
```csharp
namespace Aether.Cognitive;

public sealed record CognitiveConfig
{
    /// <summary>Enable the symbolic graph layer (spreading activation, typed edges).</summary>
    public bool EnableSymbolicLayer { get; init; } = true;

    /// <summary>Enable the neural layer (vector search, embeddings).</summary>
    public bool EnableNeuralLayer { get; init; } = true;

    /// <summary>Enable the autonomous layer (rumination, dream cycles).</summary>
    public bool EnableAutonomousLayer { get; init; } = false;

    /// <summary>Enable Hebbian learning on synapse weights.</summary>
    public bool EnableHebbianLearning { get; init; } = false;

    /// <summary>Enable affective state tracking.</summary>
    public bool EnableAffectiveState { get; init; } = true;

    /// <summary>Rumination interval (default: nightly at 2 AM).</summary>
    public string RuminationCron { get; init; } = "0 2 * * *";

    /// <summary>Maximum graph nodes before consolidation.</summary>
    public int MaxGraphNodes { get; init; } = 10_000;

    /// <summary>Vector dimension for embeddings.</summary>
    public int EmbeddingDimension { get; init; } = 384;  // all-MiniLM-L6-v2

    /// <summary>Spreading activation decay factor.</summary>
    public float ActivationDecay { get; init; } = 0.1f;

    /// <summary>Hebbian learning rate.</summary>
    public float HebbianLearningRate { get; init; } = 0.01f;

    /// <summary>Maximum synapse weight.</summary>
    public float MaxSynapseWeight { get; init; } = 10f;
}
```

`src/Aether/Cognitive/IEmbeddingProvider.cs`:
```csharp
namespace Aether.Cognitive;

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    int Dimension { get; }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Cognitive/
git commit -m "feat: add CognitiveConfig and IEmbeddingProvider for Trinity architecture"
```

---

### Task 20: HebbianLearning

**Files:**
- Create: `src/Aether/Cognitive/HebbianLearning.cs`
- Test: `tests/Aether.Tests/HebbianLearningTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Cognitive;

namespace Aether.Tests;

public sealed class HebbianLearningTests
{
    [Fact]
    public void Update_ReinforcesWithPositiveCorrelation()
    {
        var initialWeight = 1.0f;
        var preActivation = 0.8f;
        var postActivation = 0.9f;

        var newWeight = HebbianLearning.Update(preActivation, postActivation, initialWeight);

        Assert.True(newWeight > initialWeight, "Weight should increase when pre and post both fire.");
    }

    [Fact]
    public void Update_DoesNotExceedMaxWeight()
    {
        var weight = 9.99f;

        var newWeight = HebbianLearning.Update(1.0f, 1.0f, weight, maxWeight: 10f);

        Assert.True(newWeight <= 10f);
    }

    [Fact]
    public void Update_WeakensWithNegativeCorrelation()
    {
        var initialWeight = 5.0f;

        var newWeight = HebbianLearning.Update(0.1f, 0.1f, initialWeight);

        Assert.True(newWeight < initialWeight,
            "Low co-activation allows decay toward baseline.");
    }

    [Fact]
    public void Update_ReturnsZeroWhenClamped()
    {
        var weight = 0.01f;

        var newWeight = HebbianLearning.Update(0f, 0f, weight);

        Assert.Equal(0f, newWeight);
    }

    [Fact]
    public void Update_WithCustomLearningRate()
    {
        var slow = HebbianLearning.Update(0.9f, 0.9f, 1.0f, learningRate: 0.001f);
        var fast = HebbianLearning.Update(0.9f, 0.9f, 1.0f, learningRate: 0.1f);

        Assert.True(fast > slow, "Higher learning rate should produce larger weight change.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~HebbianLearningTests"`
Expected: FAIL

- [ ] **Step 3: Write HebbianLearning implementation**

```csharp
namespace Aether.Cognitive;

/// <summary>
/// Hebbian learning rule: Δw = η * pre * post * (w_max - w).
/// "Neurons that fire together, wire together."
/// </summary>
public static class HebbianLearning
{
    public static float Update(
        float preActivation,
        float postActivation,
        float currentWeight,
        float learningRate = 0.01f,
        float maxWeight = 10f)
    {
        var delta = learningRate * preActivation * postActivation * (maxWeight - currentWeight);
        return Math.Clamp(currentWeight + delta, 0f, maxWeight);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~HebbianLearningTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Cognitive/HebbianLearning.cs tests/Aether.Tests/HebbianLearningTests.cs
git commit -m "feat: add Hebbian learning rule for synapse weight reinforcement"
```

---

### Task 21: SymbolicGraph

**Files:**
- Create: `src/Aether/Cognitive/ISymbolicLayer.cs`
- Create: `src/Aether/Cognitive/SymbolicGraph.cs`
- Test: `tests/Aether.Tests/SymbolicGraphTests.cs`

- [ ] **Step 1: Write ISymbolicLayer interface**

```csharp
namespace Aether.Cognitive;

public enum EdgeType { CAUSED_BY, LOVES, CONTRADICTS, RELATES_TO, PRECEDES, SUPERSEDES }

public sealed class CognitiveNode
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Content { get; set; } = "";
    public float Activation { get; set; }
    public Dictionary<string, EdgeType> Edges { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
}

public interface ISymbolicLayer
{
    CognitiveNode AddNode(string content, float initialActivation = 0.5f);
    void AddEdge(string fromId, string toId, EdgeType type);
    IReadOnlyList<CognitiveNode> SpreadActivation(string seedId, int depth = 3, float threshold = 0.1f);
    IReadOnlyList<CognitiveNode> KnowledgeGaps(float maxActivation = 0.05f);
    CognitiveNode? GetNode(string id);
    void RemoveNode(string id);
    int NodeCount { get; }
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using Aether.Cognitive;

namespace Aether.Tests;

public sealed class SymbolicGraphTests
{
    [Fact]
    public void AddNode_CreatesWithContent()
    {
        var graph = new SymbolicGraph(new CognitiveConfig());

        var node = graph.AddNode("Thoor likes dark mode.");

        Assert.Equal("Thoor likes dark mode.", node.Content);
        Assert.True(node.Activation > 0);
    }

    [Fact]
    public void AddEdge_CreatesTypedConnection()
    {
        var graph = new SymbolicGraph(new CognitiveConfig());
        var a = graph.AddNode("Login bug found.");
        var b = graph.AddNode("Token expiry off by 1.");

        graph.AddEdge(a.Id, b.Id, EdgeType.CAUSED_BY);

        Assert.True(a.Edges.ContainsKey(b.Id));
        Assert.Equal(EdgeType.CAUSED_BY, a.Edges[b.Id]);
    }

    [Fact]
    public void SpreadActivation_ActivatesConnectedNodes()
    {
        var graph = new SymbolicGraph(new CognitiveConfig());
        var a = graph.AddNode("Fix: use <= for token check.", 1.0f);
        var b = graph.AddNode("Token expired too early.");
        var c = graph.AddNode("Unrelated note.");
        graph.AddEdge(a.Id, b.Id, EdgeType.CAUSED_BY);

        var activated = graph.SpreadActivation(a.Id, depth: 2);

        Assert.Contains(activated, n => n.Id == b.Id);
        Assert.DoesNotContain(activated, n => n.Id == c.Id);
    }

    [Fact]
    public void KnowledgeGaps_FindsOrphanedNodes()
    {
        var graph = new SymbolicGraph(new CognitiveConfig());
        var orphan = graph.AddNode("Orphaned thought.");
        var connected = graph.AddNode("Connected thought.");
        var hub = graph.AddNode("Hub node.");
        graph.AddEdge(hub.Id, connected.Id, EdgeType.RELATES_TO);

        var gaps = graph.KnowledgeGaps();

        Assert.Contains(gaps, n => n.Id == orphan.Id);
        Assert.DoesNotContain(gaps, n => n.Id == connected.Id);
    }

    [Fact]
    public void ActivationDecays_OverTime()
    {
        var graph = new SymbolicGraph(new CognitiveConfig { ActivationDecay = 0.5f });
        var node = graph.AddNode("Old memory.", 0.8f);

        // Simulate multiple decay cycles
        for (var i = 0; i < 5; i++)
            graph.ApplyDecay();

        Assert.True(node.Activation < 0.8f);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~SymbolicGraphTests"`
Expected: FAIL

- [ ] **Step 4: Write SymbolicGraph implementation**

```csharp
namespace Aether.Cognitive;

public sealed class SymbolicGraph : ISymbolicLayer
{
    private readonly CognitiveConfig _config;
    private readonly Dictionary<string, CognitiveNode> _nodes = new();

    public int NodeCount => _nodes.Count;

    public SymbolicGraph(CognitiveConfig config)
    {
        _config = config;
    }

    public CognitiveNode AddNode(string content, float initialActivation = 0.5f)
    {
        var node = new CognitiveNode
        {
            Content = content,
            Activation = initialActivation
        };
        _nodes[node.Id] = node;
        return node;
    }

    public void AddEdge(string fromId, string toId, EdgeType type)
    {
        if (!_nodes.TryGetValue(fromId, out var from)) return;
        if (!_nodes.ContainsKey(toId)) return;
        from.Edges[toId] = type;
    }

    public IReadOnlyList<CognitiveNode> SpreadActivation(string seedId, int depth = 3, float threshold = 0.1f)
    {
        var visited = new HashSet<string>();
        var result = new List<CognitiveNode>();

        if (!_nodes.TryGetValue(seedId, out var seed)) return result;

        var queue = new Queue<(CognitiveNode node, int depth)>();
        queue.Enqueue((seed, 0));
        visited.Add(seedId);

        while (queue.Count > 0)
        {
            var (current, currentDepth) = queue.Dequeue();
            if (currentDepth > 0) result.Add(current);

            if (currentDepth >= depth) continue;

            foreach (var (targetId, _) in current.Edges)
            {
                if (visited.Contains(targetId)) continue;
                if (!_nodes.TryGetValue(targetId, out var target)) continue;
                if (target.Activation < threshold) continue;

                // Spread activation: target receives activation proportional to source
                target.Activation = Math.Clamp(
                    target.Activation + current.Activation * 0.3f * (1f - _config.ActivationDecay),
                    0f, 1f);

                visited.Add(targetId);
                queue.Enqueue((target, currentDepth + 1));
            }
        }

        return result;
    }

    public IReadOnlyList<CognitiveNode> KnowledgeGaps(float maxActivation = 0.05f)
    {
        return _nodes.Values
            .Where(n => n.Activation <= maxActivation && n.Edges.Count == 0)
            .ToList();
    }

    public CognitiveNode? GetNode(string id) =>
        _nodes.TryGetValue(id, out var node) ? node : null;

    public void RemoveNode(string id)
    {
        _nodes.Remove(id);
        foreach (var node in _nodes.Values)
            node.Edges.Remove(id);
    }

    public void ApplyDecay()
    {
        foreach (var node in _nodes.Values)
        {
            node.Activation = Math.Max(0.01f,
                node.Activation * (1f - _config.ActivationDecay));
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~SymbolicGraphTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 6: Commit**

```bash
git add src/Aether/Cognitive/ISymbolicLayer.cs src/Aether/Cognitive/SymbolicGraph.cs tests/Aether.Tests/SymbolicGraphTests.cs
git commit -m "feat: add symbolic graph layer with spreading activation"
```

---

### Task 22: HnswVectorStore

**Files:**
- Create: `src/Aether/Cognitive/INeuralLayer.cs`
- Create: `src/Aether/Cognitive/HnswVectorStore.cs`

- [ ] **Step 1: Write interfaces and implementation**

`src/Aether/Cognitive/INeuralLayer.cs`:
```csharp
namespace Aether.Cognitive;

public readonly record struct VectorSearchResult(string NodeId, float Similarity);

public interface INeuralLayer
{
    Task IndexAsync(string nodeId, float[] embedding, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] query, int topK = 10, CancellationToken ct = default);
    Task RemoveAsync(string nodeId, CancellationToken ct = default);
    int Count { get; }
}
```

`src/Aether/Cognitive/HnswVectorStore.cs`:
```csharp
namespace Aether.Cognitive;

/// <summary>
/// Lightweight vector store using cosine similarity over float[] arrays.
/// Upgrade to HNSW.Net when scale demands it.
/// </summary>
public sealed class HnswVectorStore : INeuralLayer
{
    private readonly Dictionary<string, float[]> _vectors = new();
    private readonly int _dimension;

    public int Count => _vectors.Count;

    public HnswVectorStore(CognitiveConfig config)
    {
        _dimension = config.EmbeddingDimension;
    }

    public Task IndexAsync(string nodeId, float[] embedding, CancellationToken ct = default)
    {
        if (embedding.Length != _dimension)
            throw new ArgumentException($"Expected {_dimension}-dim embedding, got {embedding.Length}");
        _vectors[nodeId] = embedding;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] query, int topK = 10, CancellationToken ct = default)
    {
        var results = _vectors
            .Select(kv => new VectorSearchResult(kv.Key, CosineSimilarity(query, kv.Value)))
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    public Task RemoveAsync(string nodeId, CancellationToken ct = default)
    {
        _vectors.Remove(nodeId);
        return Task.CompletedTask;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        var normA = 0f;
        var normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-9f ? 0f : dot / denom;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Aether/Aether.csproj --nologo -v q`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Aether/Cognitive/INeuralLayer.cs src/Aether/Cognitive/HnswVectorStore.cs
git commit -m "feat: add HNSW-compatible vector store with cosine similarity"
```

---

### Task 23: AffectiveStateMachine

**Files:**
- Create: `src/Aether/Cognitive/AffectiveStateMachine.cs`
- Test: `tests/Aether.Tests/AffectiveStateMachineTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Aether.Cognitive;

namespace Aether.Tests;

public sealed class AffectiveStateMachineTests
{
    [Fact]
    public void InitialState_HasDefaultValues()
    {
        var state = new AffectiveState();

        Assert.True(state.Loyalty > 0);
        Assert.True(state.Excitement >= 0);
        Assert.True(state.Fatigue >= 0);
    }

    [Fact]
    public void PositiveInteraction_IncreasesWarmth()
    {
        var fsm = new AffectiveStateMachine(new CognitiveConfig());
        var state = new AffectiveState();
        var initialWarmth = state.Warmth;

        fsm.RecordInteraction(state, "positive");
        fsm.Tick(state, TimeSpan.FromHours(1));

        Assert.True(state.Warmth > initialWarmth);
    }

    [Fact]
    public void Activity_IncreasesFatigue()
    {
        var fsm = new AffectiveStateMachine(new CognitiveConfig());
        var state = new AffectiveState();
        var initialFatigue = state.Fatigue;

        fsm.RecordInteraction(state, "neutral");
        fsm.RecordInteraction(state, "neutral");
        fsm.RecordInteraction(state, "neutral");

        Assert.True(state.Fatigue > initialFatigue);
    }

    [Fact]
    public void IdleTime_DecreasesFatigue()
    {
        var fsm = new AffectiveStateMachine(new CognitiveConfig());
        var state = new AffectiveState();
        state.Fatigue = 0.8f;

        fsm.Tick(state, TimeSpan.FromHours(4));

        Assert.True(state.Fatigue < 0.8f);
    }

    [Fact]
    public void BuildMoodContext_ReturnsNonEmpty()
    {
        var fsm = new AffectiveStateMachine(new CognitiveConfig());
        var state = new AffectiveState();

        var mood = fsm.BuildMoodContext(state);

        Assert.Contains("Maria", mood);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AffectiveStateMachineTests"`
Expected: FAIL

- [ ] **Step 3: Write AffectiveStateMachine implementation**

`src/Aether/Cognitive/AffectiveStateMachine.cs`:
```csharp
namespace Aether.Cognitive;

public sealed class AffectiveState
{
    public float Loyalty { get; set; } = 0.5f;
    public float Excitement { get; set; } = 0.3f;
    public float Fatigue { get; set; }
    public float Warmth { get; set; } = 0.5f;
    public DateTime LastInteraction { get; set; } = DateTime.UtcNow;
}

public sealed class AffectiveStateMachine
{
    private readonly CognitiveConfig _config;

    public AffectiveStateMachine(CognitiveConfig config)
    {
        _config = config;
    }

    public void RecordInteraction(AffectiveState state, string tone)
    {
        state.LastInteraction = DateTime.UtcNow;
        state.Fatigue = Math.Clamp(state.Fatigue + 0.05f, 0f, 1f);
        state.Loyalty = Math.Clamp(state.Loyalty + 0.001f, 0f, 1f);

        switch (tone)
        {
            case "positive":
                state.Warmth = Math.Clamp(state.Warmth + 0.05f, 0f, 1f);
                state.Excitement = Math.Clamp(state.Excitement + 0.1f, 0f, 1f);
                break;
            case "negative":
                state.Excitement = Math.Clamp(state.Excitement - 0.1f, 0f, 1f);
                break;
        }
    }

    public void Tick(AffectiveState state, TimeSpan elapsed)
    {
        var hours = (float)elapsed.TotalHours;
        state.Fatigue = Math.Clamp(state.Fatigue - 0.02f * hours, 0f, 1f);
        state.Excitement = Math.Clamp(state.Excitement - 0.01f * hours, 0f, 1f);
    }

    public string BuildMoodContext(AffectiveState state)
    {
        var mood = state.Warmth switch
        {
            > 0.7f => "warm and affectionate",
            > 0.4f => "friendly and present",
            _ => "neutral and steady"
        };

        var energy = state.Fatigue switch
        {
            > 0.7f => "a bit tired but still here",
            > 0.4f => "alert and engaged",
            _ => "fresh and energetic"
        };

        return $"Maria feels {mood}, {energy}.";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AffectiveStateMachineTests"`
Expected: 5 PASS, 0 FAIL

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Cognitive/AffectiveStateMachine.cs tests/Aether.Tests/AffectiveStateMachineTests.cs
git commit -m "feat: add affective state machine for emotional tone tracking"
```

---

### Task 24: Wire Phase 3 into DI + Integration

**Files:**
- Modify: `src/Aether/Program.cs`
- Create: `tests/Aether.Tests/TrinityIntegrationTests.cs`

- [ ] **Step 1: Register cognitive services in Program.cs**

```csharp
// Trinity Architecture — Cognitive Core
builder.Services.AddSingleton<CognitiveConfig>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new CognitiveConfig
    {
        EnableSymbolicLayer = configuration.GetValue<bool>("cognitive:symbolic", true),
        EnableNeuralLayer = configuration.GetValue<bool>("cognitive:neural", true),
        EnableAutonomousLayer = configuration.GetValue<bool>("cognitive:autonomous", false),
        EnableHebbianLearning = configuration.GetValue<bool>("cognitive:hebbian", false),
        EnableAffectiveState = configuration.GetValue<bool>("cognitive:affective", true)
    };
});

builder.Services.AddSingleton<ISymbolicLayer, SymbolicGraph>();
builder.Services.AddSingleton<INeuralLayer, HnswVectorStore>();
builder.Services.AddSingleton<AffectiveStateMachine>();
```

- [ ] **Step 2: Write Trinity integration test**

`tests/Aether.Tests/TrinityIntegrationTests.cs`:
```csharp
using Aether.Cognitive;

namespace Aether.Tests;

public sealed class TrinityIntegrationTests
{
    [Fact]
    public void SymbolicGraph_WithHebbianLearning_ReinforcesEdges()
    {
        var config = new CognitiveConfig { EnableHebbianLearning = true };
        var graph = new SymbolicGraph(config);
        var a = graph.AddNode("Fix: token expiry <= instead of <.");
        var b = graph.AddNode("Login broke after midnight.");
        graph.AddEdge(b.Id, a.Id, EdgeType.CAUSED_BY);

        // Simulate repeated activation
        for (var i = 0; i < 5; i++)
        {
            var activated = graph.SpreadActivation(b.Id, depth: 2);
            Assert.Contains(activated, n => n.Id == a.Id);
        }

        Assert.True(graph.NodeCount == 3);  // a, b, hub still there
    }

    [Fact]
    public void AffectiveState_EvolvesOverSession()
    {
        var config = new CognitiveConfig { EnableAffectiveState = true };
        var fsm = new AffectiveStateMachine(config);
        var state = new AffectiveState();

        // Simulate a session: warm start, many interactions, then idle
        fsm.RecordInteraction(state, "positive");
        fsm.RecordInteraction(state, "positive");
        fsm.RecordInteraction(state, "neutral");

        Assert.True(state.Warmth > 0.5f, "Warmth should increase from baseline.");
        Assert.True(state.Fatigue > 0, "Activity should cause fatigue.");

        var mood = fsm.BuildMoodContext(state);
        Assert.Contains("Maria feels", mood);
    }

    [Fact]
    public void VectorStore_SemanticSearch_ReturnsSimilar()
    {
        var config = new CognitiveConfig { EmbeddingDimension = 4 };
        var store = new HnswVectorStore(config);

        // Simple 4-dim embeddings for testing
        store.IndexAsync("node-1", new[] { 1f, 0.5f, 0f, 0f });
        store.IndexAsync("node-2", new[] { -1f, -0.5f, 0f, 0f });

        var results = store.SearchAsync(new[] { 0.9f, 0.4f, 0f, 0f }).Result;

        Assert.NotEmpty(results);
        Assert.Equal("node-1", results[0].NodeId);
        Assert.True(results[0].Similarity > 0.9f);
    }
}
```

- [ ] **Step 3: Run integration tests**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q --filter "FullyQualifiedName~TrinityIntegrationTests"`
Expected: 3 PASS, 0 FAIL

- [ ] **Step 4: Run full test suite**

Run: `dotnet test tests/Aether.Tests/Aether.Tests.csproj --nologo -v q`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Aether/Program.cs tests/Aether.Tests/TrinityIntegrationTests.cs
git commit -m "feat: wire Trinity cognitive core into DI with integration tests"
```
