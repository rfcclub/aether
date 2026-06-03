using System.Collections.Concurrent;
using Aether.Agent;
using Aether.Channels;
using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Scheduling;

public sealed class CronSchedulerService : BackgroundService
{
    private readonly string _cronDir;
    private readonly AetherSoul _soul;
    private readonly IChannel _channel;
    private readonly ILogger<CronSchedulerService> _logger;
    private readonly ConcurrentDictionary<string, CronTaskState> _tasks = new();
    private FileSystemWatcher? _watcher;

    private const int MinIntervalSeconds = 60;

    public CronSchedulerService(string cronDir, AetherSoul soul, IChannel channel, ILogger<CronSchedulerService> logger)
    {
        _cronDir = cronDir;
        _soul = soul;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_cronDir);
        await LoadAllTasksAsync(ct);

        // Watch for file changes to reload tasks
        _watcher = new FileSystemWatcher(_cronDir, "*.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnCronFileChanged;
        _watcher.Created += OnCronFileChanged;
        _watcher.Deleted += OnCronFileChanged;
        _watcher.Renamed += OnCronFileChanged;

        _logger.LogInformation("Cron scheduler started with {Count} tasks in {Dir}", _tasks.Count, _cronDir);

        // Main loop: check for due tasks
        while (!ct.IsCancellationRequested)
        {
            foreach (var (path, state) in _tasks)
            {
                if (state.IsRunning) continue;
                if (state.Definition is not { Enabled: true }) continue;
                if (DateTime.UtcNow < state.NextFire) continue;

                _ = ExecuteTaskAsync(state, ct);
            }

            await Task.Delay(1000, ct);
        }
    }

    public void AddTask(string key, CronTaskDefinition definition)
    {
        var nextFire = GetNextOccurrence(definition.Schedule);
        _tasks[key] = new CronTaskState(definition, nextFire);
        _logger.LogInformation("Cron task '{Key}' registered: {Schedule} → next fire at {Next}",
            key, definition.Schedule, nextFire.ToLocalTime());
    }

    private async Task LoadAllTasksAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_cronDir)) return;

        foreach (var file in Directory.EnumerateFiles(_cronDir, "*.md"))
        {
            var definition = await CronFrontmatterParser.ParseAsync(file, _logger);
            if (definition is null) continue;

            var nextFire = GetNextOccurrence(definition.Schedule);
            _tasks[file] = new CronTaskState(definition, nextFire);

            _logger.LogInformation("Cron task '{Name}' scheduled: {Schedule} → next fire at {Next}",
                Path.GetFileName(file), definition.Schedule, nextFire.ToLocalTime());
        }
    }

    private async Task ExecuteTaskAsync(CronTaskState state, CancellationToken ct)
    {
        state.IsRunning = true;
        try
        {
            _logger.LogInformation("Cron task '{Name}' firing", Path.GetFileName(state.Definition!.FilePath));
            var response = await _soul.ProcessTaskAsync(state.Definition.Agent, state.Definition.Body, ct);

            if (!response.Content.Contains("HEARTBEAT_OK"))
            {
                var chatId = FormatChatId(state.Definition.Channel);
                await _channel.SendMessageAsync(chatId, response.Content, ct);
            }
            else
            {
                _logger.LogDebug("Cron task '{Name}' heartbeat OK, suppressed", Path.GetFileName(state.Definition.FilePath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cron task '{Name}' failed", Path.GetFileName(state.Definition!.FilePath));
        }
        finally
        {
            state.NextFire = GetNextOccurrence(state.Definition!.Schedule);
            state.IsRunning = false;
        }
    }

    private DateTime GetNextOccurrence(string schedule)
    {
        try
        {
            var expression = CronExpression.Parse(schedule, CronFormat.Standard);
            var next = expression.GetNextOccurrence(DateTime.UtcNow, inclusive: false)
                       ?? DateTime.UtcNow.AddHours(1);

            // Enforce minimum interval
            var minNext = DateTime.UtcNow.AddSeconds(MinIntervalSeconds);
            if (next < minNext)
            {
                _logger.LogWarning("Cron schedule '{Schedule}' too frequent, enforcing {Sec}s minimum", schedule, MinIntervalSeconds);
                next = minNext;
            }

            return next;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid cron expression '{Schedule}', defaulting to hourly", schedule);
            return DateTime.UtcNow.AddHours(1);
        }
    }

    private string FormatChatId(string channel) => channel switch
    {
        "telegram" => "telegram:6713734957",
        _ => $"{channel}:default"
    };

    private async void OnCronFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (e.Name is null) return;

            _logger.LogDebug("Cron file changed: {Path}", e.Name);
            var path = Path.Combine(_cronDir, e.Name);
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                _tasks.TryRemove(path, out _);
                return;
            }

            var definition = await CronFrontmatterParser.ParseAsync(path, _logger);
            if (definition is not null)
                _tasks[path] = new CronTaskState(definition, GetNextOccurrence(definition.Schedule));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload cron task {Path}", e.Name);
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }

    private sealed class CronTaskState
    {
        public CronTaskDefinition? Definition { get; }
        public DateTime NextFire { get; set; }
        public bool IsRunning { get; set; }

        public CronTaskState(CronTaskDefinition definition, DateTime nextFire)
        {
            Definition = definition;
            NextFire = nextFire;
        }
    }
}
