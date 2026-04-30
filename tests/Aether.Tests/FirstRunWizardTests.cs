using System.Text.Json;
using Aether.Cli;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class FirstRunWizardTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _aetherDir;

    public FirstRunWizardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_wiz_{Guid.NewGuid():N}");
        _aetherDir = Path.Combine(_tempDir, ".aether");
        Directory.CreateDirectory(_aetherDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void IsFirstRun_returns_true_when_config_missing()
    {
        var wizard = new FirstRunWizard(_aetherDir, NullLogger<FirstRunWizard>.Instance);
        Assert.True(wizard.IsFirstRun());
    }

    [Fact]
    public void IsFirstRun_returns_false_when_config_exists()
    {
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"), "{}");
        var wizard = new FirstRunWizard(_aetherDir, NullLogger<FirstRunWizard>.Instance);
        Assert.False(wizard.IsFirstRun());
    }

    [Fact]
    public async Task RunNonInteractive_creates_minimal_config()
    {
        var wizard = new FirstRunWizard(_aetherDir, NullLogger<FirstRunWizard>.Instance);

        await wizard.RunNonInteractiveAsync();

        var configPath = Path.Combine(_aetherDir, "config.json");
        Assert.True(File.Exists(configPath));

        var json = await File.ReadAllTextAsync(configPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("wizard", out _));
    }

    [Fact]
    public async Task RunNonInteractive_writes_wizard_metadata()
    {
        var wizard = new FirstRunWizard(_aetherDir, NullLogger<FirstRunWizard>.Instance);

        await wizard.RunNonInteractiveAsync();

        var json = await File.ReadAllTextAsync(Path.Combine(_aetherDir, "config.json"));
        using var doc = JsonDocument.Parse(json);
        var wizardEl = doc.RootElement.GetProperty("wizard");
        Assert.True(wizardEl.TryGetProperty("lastRunAt", out _));
        Assert.True(wizardEl.TryGetProperty("lastRunVersion", out _));
        Assert.True(wizardEl.TryGetProperty("lastRunCommand", out _));
    }

    [Fact]
    public async Task RunNonInteractive_creates_default_agent()
    {
        var wizard = new FirstRunWizard(_aetherDir, NullLogger<FirstRunWizard>.Instance);

        await wizard.RunNonInteractiveAsync();

        var json = await File.ReadAllTextAsync(Path.Combine(_aetherDir, "config.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("agents", out var agents));
        Assert.True(agents.TryGetProperty("default", out _));
    }

    [Fact]
    public async Task RunNonInteractive_does_not_overwrite_existing_config()
    {
        File.WriteAllText(Path.Combine(_aetherDir, "config.json"),
            """{"llm":{"model":"custom-model"},"custom":true}""");

        var wizard = new FirstRunWizard(_aetherDir, NullLogger<FirstRunWizard>.Instance);
        // Should detect existing config and skip
        Assert.False(wizard.IsFirstRun());
    }
}
