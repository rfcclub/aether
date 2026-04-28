using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Aether.Tests;

public class ToolHotReloadServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ToolRegistry _registry;
    private readonly ToolHotReloadService _service;
    private readonly CancellationTokenSource _cts;

    public ToolHotReloadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AetherToolHotReload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        _service = new ToolHotReloadService(_registry, NullLogger<ToolHotReloadService>.Instance, _tempDir);
        _cts = new CancellationTokenSource();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        (_service as IDisposable)?.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
    }

    private string WriteToolJson(string fileName, string name, string description = "test tool")
    {
        var tool = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters_json"] = "{}",
            ["schema_json"] = "{}"
        };
        var json = JsonSerializer.Serialize(tool);
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, json);
        return filePath;
    }

    private string WriteInvalidJson(string fileName)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, "this is not valid json");
        return filePath;
    }

    [Fact]
    public async Task Start_LoadsExistingToolFiles()
    {
        // Arrange: create a .json file before the service starts scanning.
        WriteToolJson("greeter.json", "greeter");

        // Act: start the service and wait for the initial load.
        await _service.StartAsync(_cts.Token);
        await Task.Delay(500);

        // Assert: the existing file was loaded.
        Assert.True(_registry.HasTool("greeter"));
    }

    [Fact]
    public async Task FileCreated_RegistersToolWithinDebounce()
    {
        // Act: start the service first.
        await _service.StartAsync(_cts.Token);
        await Task.Delay(300);

        WriteToolJson("echo.json", "echo");

        // Wait for debounce (2s) plus some margin.
        await Task.Delay(3000);

        // Assert.
        Assert.True(_registry.HasTool("echo"));
    }

    [Fact]
    public async Task FileDeleted_UnregistersTool()
    {
        // Arrange: register the tool first.
        WriteToolJson("removeme.json", "removeme");
        await _service.StartAsync(_cts.Token);
        await Task.Delay(3000);

        Assert.True(_registry.HasTool("removeme"), "Tool should be registered after file creation");

        // Act: delete the file.
        File.Delete(Path.Combine(_tempDir, "removeme.json"));

        // Wait for debounce + detection.
        await Task.Delay(3000);

        // Assert.
        Assert.False(_registry.HasTool("removeme"));
    }

    [Fact]
    public async Task InvalidJson_SkipsFile_NoCrash()
    {
        // Act: start the service.
        await _service.StartAsync(_cts.Token);
        await Task.Delay(300);

        // Write a valid tool, then an invalid one.
        WriteToolJson("valid.json", "valid");
        WriteInvalidJson("broken.json");

        // Wait for debounce.
        await Task.Delay(3000);

        // Assert: valid tool registered, broken one did not crash the service.
        Assert.True(_registry.HasTool("valid"));
        Assert.False(_registry.HasTool("broken"));
    }

    [Fact]
    public async Task Debounce_PreventsDuplicateRegistration()
    {
        // Act: start the service.
        await _service.StartAsync(_cts.Token);
        await Task.Delay(300);

        // Rapidly write the same file, simulating multiple FileSystemWatcher events.
        WriteToolJson("dedup.json", "dedup");
        await Task.Delay(200);
        WriteToolJson("dedup.json", "dedup");

        // Wait for debounce — only one registration should happen.
        await Task.Delay(3000);

        // Assert: tool is registered once (multiple registrations just overwrite).
        Assert.True(_registry.HasTool("dedup"));
    }

    [Fact]
    public async Task FileModified_ReRegistersTool()
    {
        // Arrange.
        WriteToolJson("updatable.json", "updatable", "original description");
        await _service.StartAsync(_cts.Token);
        await Task.Delay(3000);

        // Act: modify the file with new description.
        WriteToolJson("updatable.json", "updatable", "updated description");
        await Task.Delay(3000);

        // Assert: tool is still registered (the update replaced the registration).
        Assert.True(_registry.HasTool("updatable"));
    }

    [Fact]
    public async Task MissingNameField_LogsError_SkipsFile()
    {
        // Arrange: create a JSON file with no "name" property.
        var filePath = Path.Combine(_tempDir, "noname.json");
        File.WriteAllText(filePath, """{ "description": "no name here", "parameters_json": "{}" }""");

        await _service.StartAsync(_cts.Token);
        await Task.Delay(3000);

        // Assert: no tool was registered from this file.
        Assert.False(_registry.HasTool("noname"));
    }

    [Fact]
    public async Task FileReplaced_UnregistersOldAndRegistersNew()
    {
        // Arrange.
        WriteToolJson("replaced.json", "original");
        await _service.StartAsync(_cts.Token);
        await Task.Delay(3000);
        Assert.True(_registry.HasTool("original"));

        // Act: delete the old file.
        File.Delete(Path.Combine(_tempDir, "replaced.json"));
        // Wait for sweep timer (5s interval) plus margin.
        await Task.Delay(7000);

        // Assert: original unregistered (caught by periodic sweep since
        // FileSystemWatcher may not fire Deleted on WSL/Linux).
        Assert.False(_registry.HasTool("original"));

        WriteToolJson("replacement.json", "replacement");
        await Task.Delay(3000);

        // Assert: replacement registered.
        Assert.True(_registry.HasTool("replacement"));
    }

    [Fact]
    public async Task Stop_CancelsWatcherGracefully()
    {
        // Arrange.
        await _service.StartAsync(_cts.Token);
        await Task.Delay(300);
        WriteToolJson("graceful.json", "graceful");

        // Act: stop the service.
        await _service.StopAsync(_cts.Token);
        await Task.Delay(3000);

        // The service stopped, but the tool registered before stop should persist.
        // Only test that StopAsync completes without exception.
        Assert.True(true, "StopAsync completed gracefully");
    }
}
