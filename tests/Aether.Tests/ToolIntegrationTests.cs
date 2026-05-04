using System.Text.Json;
using Aether.Config;
using Aether.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Tests;

public sealed class ToolIntegrationTests : IDisposable
{
    private readonly string _workspace;
    private readonly SandboxContext _sandbox;

    public ToolIntegrationTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"aether_int_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        _sandbox = new SandboxContext(_workspace, new SpecToolsSection
        {
            File = new SpecFileTool { AllowWrites = true }
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { }
    }

    [Fact]
    public async Task ReadWriteEditGrepPipeline()
    {
        // Arrange: set up real tools
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

        var writeTool = new WriteTool(loggerFactory.CreateLogger<WriteTool>());
        var readTool = new ReadTool(loggerFactory.CreateLogger<ReadTool>());
        var editTool = new EditTool(loggerFactory.CreateLogger<EditTool>());
        var grepTool = new GrepTool(loggerFactory.CreateLogger<GrepTool>());

        var filePath = Path.Combine(_workspace, "notes.txt");

        // Act 1: Write a file
        var writeArgs = JsonDocument.Parse($$"""
            {"path": "notes.txt", "content": "Hello world\nThis is a test file\nAnother line here"}
            """).RootElement;
        var writeResult = await writeTool.ExecuteAsync(writeArgs, _sandbox, CancellationToken.None);
        Assert.NotNull(writeResult);
        Assert.True(File.Exists(filePath));

        // Act 2: Read it back
        var readArgs = JsonDocument.Parse("""{"path": "notes.txt"}""").RootElement;
        var readResult = await readTool.ExecuteAsync(readArgs, _sandbox, CancellationToken.None);
        var content = readResult.ToString()!;
        Assert.Contains("Hello world", content);
        Assert.Contains("Another line here", content);

        // Act 3: Edit — replace text
        var editArgs = JsonDocument.Parse("""{"path": "notes.txt", "old_string": "Hello world", "new_string": "Greetings universe"}""").RootElement;
        var editResult = await editTool.ExecuteAsync(editArgs, _sandbox, CancellationToken.None);
        Assert.NotNull(editResult);

        // Verify edit
        var readAgain = await readTool.ExecuteAsync(readArgs, _sandbox, CancellationToken.None);
        var editedContent = readAgain.ToString()!;
        Assert.Contains("Greetings universe", editedContent);
        Assert.DoesNotContain("Hello world", editedContent);

        // Act 4: Grep for "test"
        var grepArgs = JsonDocument.Parse("""{"pattern": "test", "path": "."}""").RootElement;
        var grepResult = await grepTool.ExecuteAsync(grepArgs, _sandbox, CancellationToken.None);
        var grepOutput = grepResult.ToString()!;
        Assert.Contains("notes.txt", grepOutput);
    }

    [Fact]
    public async Task BashExecutesInSandbox()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var bashTool = new BashTool(loggerFactory.CreateLogger<BashTool>());

        // Write a test file first
        File.WriteAllText(Path.Combine(_workspace, "hello.txt"), "hello");

        var args = JsonDocument.Parse("""{"command": "ls *.txt && cat hello.txt"}""").RootElement;
        var result = (BashTool.BashResult)await bashTool.ExecuteAsync(args, _sandbox, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello.txt", result.Output);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task GlobFindsFiles()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var globTool = new GlobTool(loggerFactory.CreateLogger<GlobTool>());

        File.WriteAllText(Path.Combine(_workspace, "a.cs"), "");
        File.WriteAllText(Path.Combine(_workspace, "b.cs"), "");
        File.WriteAllText(Path.Combine(_workspace, "readme.md"), "");

        var args = JsonDocument.Parse("""{"pattern": "*.cs"}""").RootElement;
        var result = await globTool.ExecuteAsync(args, _sandbox, CancellationToken.None);
        var output = result.ToString()!;

        Assert.Contains("a.cs", output);
        Assert.Contains("b.cs", output);
        Assert.DoesNotContain("readme.md", output);
    }

    [Fact]
    public async Task SandboxBlocksOutsidePath()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var readTool = new ReadTool(loggerFactory.CreateLogger<ReadTool>());

        var args = JsonDocument.Parse("""{"path": "/etc/passwd"}""").RootElement;

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => readTool.ExecuteAsync(args, _sandbox, CancellationToken.None));
        Assert.Contains("not permitted", ex.Message);
    }

    [Fact]
    public async Task BashRespectsCommandAllowlist()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var bashTool = new BashTool(loggerFactory.CreateLogger<BashTool>());

        // Sandbox with restricted commands
        var restricted = new SandboxContext(_workspace, new Config.SpecToolsSection
        {
            Shell = new Config.SpecShellTool
            {
                AllowedCommands = new List<string> { "echo" }
            }
        });

        // echo should work
        var okArgs = JsonDocument.Parse("""{"command": "echo hello"}""").RootElement;
        var okResult = (BashTool.BashResult)await bashTool.ExecuteAsync(okArgs, restricted, CancellationToken.None);
        Assert.Equal(0, okResult.ExitCode);

        // cat should be denied
        File.WriteAllText(Path.Combine(_workspace, "test.txt"), "secret");
        var badArgs = JsonDocument.Parse("""{"command": "cat test.txt"}""").RootElement;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => bashTool.ExecuteAsync(badArgs, restricted, CancellationToken.None));
    }
}
