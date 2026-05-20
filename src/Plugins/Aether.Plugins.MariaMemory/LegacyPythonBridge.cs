using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class LegacyPythonBridge
{
    private readonly string _pluginDir;
    private readonly string _pythonPath;
    private readonly ILogger _logger;

    public LegacyPythonBridge(string pluginDir, ILogger logger, string pythonPath = "python3")
    {
        _pluginDir = pluginDir;
        _pythonPath = pythonPath;
        _logger = logger;
    }

    public async Task<string> CallLegacyToolAsync(string toolName, object args, CancellationToken ct = default)
    {
        var bridgeScript = Path.Combine(_pluginDir, "bridge_logic.py");
        if (!File.Exists(bridgeScript))
            return "{\"success\": false, \"error\": \"Legacy bridge script not found\"}";

        var argsJson = JsonSerializer.Serialize(args);
        
        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{bridgeScript}\" \"{toolName}\" '{argsJson}'",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "{\"success\": false, \"error\": \"Failed to start Python process\"}";

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("Legacy tool {Tool} failed: {Error}", toolName, error);
            return JsonSerializer.Serialize(new { success = false, error, exitCode = process.ExitCode });
        }

        return output;
    }
}
