using System.Collections.Concurrent;
using Aether.Agents;
using Aether.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Scheduling;

public sealed class KairosWatchService : BackgroundService
{
    private readonly string _workspaceDir;
    private readonly KairosConfig _config;
    private readonly IChannel _channel;
    private readonly ILogger<KairosWatchService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();
    private FileSystemWatcher? _watcher;

    public KairosWatchService(string workspaceDir, KairosConfig config, IChannel channel, ILogger<KairosWatchService> logger)
    {
        _workspaceDir = workspaceDir;
        _config = config;
        _channel = channel;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken ct)
    {
        if (!_config.Enabled || _config.Rules is not { Count: > 0 })
        {
            _logger.LogInformation("KAIROS disabled for workspace {Dir}", _workspaceDir);
            return Task.CompletedTask;
        }

        if (!Directory.Exists(_workspaceDir))
        {
            _logger.LogWarning("KAIROS workspace not found: {Dir}", _workspaceDir);
            return Task.CompletedTask;
        }

        _watcher = new FileSystemWatcher(_workspaceDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
            InternalBufferSize = 65536
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Error += OnWatcherError;

        _logger.LogInformation("KAIROS watching {Dir} with {Count} rules", _workspaceDir, _config.Rules.Count);

        // Keep alive until cancelled
        ct.Register(() => _watcher?.Dispose());
        return Task.CompletedTask;
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_workspaceDir, e.FullPath);
            if (string.IsNullOrEmpty(relativePath) || relativePath.StartsWith('.')) return;
            if (!relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return;

            var matchedRule = _config.Rules!.Find(r => MatchesGlob(relativePath, r.Watch));
            if (matchedRule is null) return;

            // Cooldown check
            var lastNotify = _cooldowns.GetOrAdd(matchedRule.Watch, DateTime.MinValue);
            var since = DateTime.UtcNow - lastNotify;
            if (since.TotalSeconds < matchedRule.CooldownSeconds)
            {
                _logger.LogDebug("KAIROS cooldown: {Rule} ({Sec:F0}s since last)", matchedRule.Watch, since.TotalSeconds);
                return;
            }

            _cooldowns[matchedRule.Watch] = DateTime.UtcNow;

            var notification = $"KAIROS: {relativePath} {e.ChangeType.ToString().ToLowerInvariant()} [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC]";
            var chatId = FormatChatId(matchedRule.Channel);
            await _channel.SendMessageAsync(chatId, notification, CancellationToken.None);

            _logger.LogInformation("KAIROS notified: {Path} {Change}", relativePath, e.ChangeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KAIROS notification failed for {Path}", e.FullPath);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "KAIROS FileSystemWatcher error, reinitializing");
        try
        {
            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(_workspaceDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
                InternalBufferSize = 65536
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Error += OnWatcherError;
        }
        catch (Exception ex2)
        {
            _logger.LogError(ex2, "KAIROS reinitialization failed");
        }
    }

    private static bool MatchesGlob(string path, string pattern)
    {
        // Simple glob: *.md, research/*.md, **/*.md
        if (pattern.StartsWith("**/"))
        {
            var suffix = pattern[3..];
            return path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        var starIdx = pattern.IndexOf('*');
        if (starIdx < 0) return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);

        var prefix = pattern[..starIdx];
        var suffixGlob = pattern[(starIdx + 1)..];

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var remaining = path[prefix.Length..];
        return remaining.EndsWith(suffixGlob, StringComparison.OrdinalIgnoreCase)
               || suffixGlob == string.Empty;
    }

    private static string FormatChatId(string channel) => channel switch
    {
        "telegram" => "telegram:6713734957",
        _ => $"{channel}:default"
    };

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
