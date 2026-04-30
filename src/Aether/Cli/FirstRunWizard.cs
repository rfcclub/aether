using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Cli;

public sealed class FirstRunWizard
{
    private readonly string _aetherDir;
    private readonly ILogger<FirstRunWizard> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FirstRunWizard(string aetherDir, ILogger<FirstRunWizard> logger)
    {
        _aetherDir = aetherDir;
        _logger = logger;
    }

    public bool IsFirstRun()
    {
        return !File.Exists(Path.Combine(_aetherDir, "config.json"));
    }

    public async Task RunNonInteractiveAsync(CancellationToken ct = default)
    {
        var configPath = Path.Combine(_aetherDir, "config.json");
        if (File.Exists(configPath))
        {
            _logger.LogInformation("Config already exists, skipping wizard");
            return;
        }

        var config = new Dictionary<string, object?>
        {
            ["llm"] = new Dictionary<string, object?>
            {
                ["provider"] = "openrouter",
                ["model"] = ""
            },
            ["wizard"] = new Dictionary<string, object?>
            {
                ["lastRunAt"] = DateTime.UtcNow.ToString("O"),
                ["lastRunVersion"] = ThisAssembly.AssemblyVersion,
                ["lastRunCommand"] = "non-interactive"
            },
            ["meta"] = new Dictionary<string, object?>
            {
                ["lastTouchedVersion"] = ThisAssembly.AssemblyVersion
            },
            ["agents"] = new Dictionary<string, object?>
            {
                ["default"] = new Dictionary<string, object?>
                {
                    ["name"] = "default",
                    ["workspace"] = Path.Combine(_aetherDir, "workspaces", "default"),
                    ["enabled"] = true
                }
            }
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct);
    }
}

internal static class ThisAssembly
{
    public const string AssemblyVersion = "0.1.0";
}
