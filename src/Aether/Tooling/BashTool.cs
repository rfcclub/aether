using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public sealed class BashTool : IToolImplementation
{
    private readonly ILogger<BashTool> _logger;
    private const int MaxOutputBytes = 65536; // 64KB

    public string Name => "bash";
    public string Description => "Execute a shell command";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "command": { "type": "string", "description": "Shell command to execute" }
            },
            "required": ["command"]
        }
        """).RootElement;

    public BashTool(ILogger<BashTool> logger) => _logger = logger;

    public async Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct)
    {
        var command = args.GetProperty("command").GetString()!;

        // Check allowed commands if configured
        if (sandbox.AllowedCommands.Count > 0)
        {
            var cmdName = command.Split(' ', 2)[0];
            if (!sandbox.AllowedCommands.Contains(cmdName, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Command '{cmdName}' not permitted. Allowed: {string.Join(", ", sandbox.AllowedCommands)}");
        }

        var timeout = sandbox.BashTimeoutSeconds > 0 ? sandbox.BashTimeoutSeconds : 60;

        var psi = CreateShellStartInfo(command, sandbox.WorkspacePath);

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        try
        {
            process.Start();

            var readTask = Task.Run(async () =>
            {
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();
                await Task.WhenAll(outTask, errTask);
                stdout.Append(outTask.Result);
                stderr.Append(errTask.Result);
            }, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            await process.WaitForExitAsync(cts.Token);
            // Give readTask a moment to finish consuming output
            await Task.WhenAny(readTask, Task.Delay(500, CancellationToken.None));
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"Command timed out after {timeout}s.");
        }

        var exitCode = process.ExitCode;
        var output = stdout.ToString();
        var errors = stderr.ToString();

        var combined = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(output))
            combined.Append(output);
        if (!string.IsNullOrEmpty(errors))
        {
            if (combined.Length > 0) combined.AppendLine();
            combined.Append("[stderr] ").Append(errors);
        }

        var result = combined.ToString();
        if (result.Length > MaxOutputBytes)
            result = result[..MaxOutputBytes] + "\n[Output truncated at 64KB]";

        return new BashResult(exitCode, result);
    }

    public record BashResult(int ExitCode, string Output)
    {
        public override string ToString() =>
            ExitCode == 0 ? Output : $"[exit {ExitCode}]\n{Output}";
    }

    private static ProcessStartInfo CreateShellStartInfo(string command, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/C");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        return psi;
    }
}

internal static class ProcessExtensions
{
    public static Task WaitForExitAsync(this Process process, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource();
        ct.Register(() => tcs.TrySetCanceled(ct));
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => tcs.TrySetResult();
        if (process.HasExited) tcs.TrySetResult();
        return tcs.Task;
    }
}
