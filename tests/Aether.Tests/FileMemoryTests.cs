using Aether.Memory;

namespace Aether.Tests;

public class FileMemoryTests : IDisposable
{
    private readonly string _memoryRoot;

    public FileMemoryTests()
    {
        _memoryRoot = Path.Combine(Path.GetTempPath(), $"aether-filemem-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_memoryRoot);
    }

    [Fact]
    public async Task LoadContext_WithGlobalAndGroup_Files_ReturnsBoth()
    {
        Directory.CreateDirectory(Path.Combine(_memoryRoot, "main"));
        await File.WriteAllTextAsync(Path.Combine(_memoryRoot, "CLAUDE.md"), "global context");
        await File.WriteAllTextAsync(Path.Combine(_memoryRoot, "main", "CLAUDE.md"), "main context");

        var memory = new FileMemory(_memoryRoot);
        var context = await memory.LoadContextAsync("main");

        Assert.Contains("global context", context);
        Assert.Contains("main context", context);
    }

    [Fact]
    public async Task LoadContext_OnlyGlobal_ReturnsGlobal()
    {
        Directory.CreateDirectory(Path.Combine(_memoryRoot, "main"));
        await File.WriteAllTextAsync(Path.Combine(_memoryRoot, "CLAUDE.md"), "global only");

        var memory = new FileMemory(_memoryRoot);
        var context = await memory.LoadContextAsync("main");

        Assert.Contains("global only", context);
    }

    [Fact]
    public async Task LoadContext_OnlyGroup_ReturnsGroup()
    {
        Directory.CreateDirectory(Path.Combine(_memoryRoot, "main"));
        await File.WriteAllTextAsync(Path.Combine(_memoryRoot, "main", "CLAUDE.md"), "group only");

        var memory = new FileMemory(_memoryRoot);
        var context = await memory.LoadContextAsync("main");

        Assert.Contains("group only", context);
    }

    [Fact]
    public async Task LoadContext_NoFiles_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_memoryRoot, "main"));
        var memory = new FileMemory(_memoryRoot);
        var context = await memory.LoadContextAsync("main");

        Assert.Empty(context);
    }

    [Fact]
    public async Task LoadContext_NonexistentGroup_OnlyGlobal()
    {
        await File.WriteAllTextAsync(Path.Combine(_memoryRoot, "CLAUDE.md"), "global");
        var memory = new FileMemory(_memoryRoot);
        var context = await memory.LoadContextAsync("nonexistent");

        Assert.Contains("global", context);
        Assert.DoesNotContain("nonexistent", context);
    }

    public void Dispose()
    {
        if (Directory.Exists(_memoryRoot))
            Directory.Delete(_memoryRoot, recursive: true);
    }
}
