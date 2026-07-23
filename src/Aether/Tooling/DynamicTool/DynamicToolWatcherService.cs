using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling.DynamicTool;

/// <summary>
/// Watches the tools directory for .cs files and hot-reloads dynamic tools.
/// Uses FileSystemWatcher with debounce to batch rapid changes.
/// </summary>
public sealed class DynamicToolWatcherService : IHostedService, IDisposable
{
    private readonly string _toolsDirectory;
    private readonly ILogger<DynamicToolWatcherService> _logger;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private bool _disposed;

    // Track compiled tools: fileName -> (type, instance)
    internal readonly ConcurrentDictionary<string, (Type Type, IDynamicTool Instance)> LoadedTools = new();

    // Debounce state
    private const int DebounceMs = 2000;
    private readonly HashSet<string> _pendingChanges = new();
    private readonly object _lock = new();

    public IReadOnlyCollection<IDynamicTool> Tools =>
        LoadedTools.Values.Select(t => t.Instance).ToList().AsReadOnly();

    public DynamicToolWatcherService(
        string toolsDirectory,
        ILogger<DynamicToolWatcherService> logger)
    {
        _toolsDirectory = toolsDirectory;
        _logger = logger;

        if (!Directory.Exists(_toolsDirectory))
        {
            Directory.CreateDirectory(_toolsDirectory);
            _logger.LogInformation("Created tools directory: {Dir}", _toolsDirectory);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Load existing .cs files
        foreach (var csFile in Directory.GetFiles(_toolsDirectory, "*.cs"))
        {
            LoadToolFile(csFile);
        }

        // Set up file watcher for hot-reload
        _watcher = new FileSystemWatcher(_toolsDirectory, "*.cs")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };

        _watcher.Created += OnToolFileChanged;
        _watcher.Changed += OnToolFileChanged;
        _watcher.Deleted += OnToolFileDeleted;
        _watcher.Error += OnWatcherError;

        _logger.LogInformation("DynamicToolWatcher started, watching: {Dir}", _toolsDirectory);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        _logger.LogInformation("DynamicToolWatcher stopped");
        return Task.CompletedTask;
    }

    private void OnToolFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingChanges.Add(e.FullPath);
            ResetDebounce();
        }
    }

    private void OnToolFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            if (LoadedTools.TryRemove(e.Name ?? "", out var removed))
            {
                _logger.LogInformation("Unloaded tool: {Name} ({Tool})",
                    e.Name, removed.Instance.Name);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "DynamicTool file watcher error");
    }

    private void ResetDebounce()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(DebounceElapsed, null, DebounceMs, Timeout.Infinite);
    }

    private void DebounceElapsed(object? state)
    {
        string[] filesToProcess;
        lock (_lock)
        {
            filesToProcess = _pendingChanges.ToArray();
            _pendingChanges.Clear();
        }

        foreach (var file in filesToProcess)
        {
            if (File.Exists(file))
            {
                LoadToolFile(file);
            }
        }
    }

    private void LoadToolFile(string csFilePath)
    {
        var fileName = Path.GetFileName(csFilePath);

        try
        {
            var (assembly, errors) = DynamicToolCompiler.Compile(csFilePath, _logger);

            if (assembly == null)
            {
                _logger.LogWarning("Failed to compile {File}: {Errors}",
                    fileName, string.Join("; ", errors ?? Array.Empty<string>()));
                return;
            }

            var toolTypes = DynamicToolCompiler.FindDynamicTools(assembly).ToList();

            if (toolTypes.Count == 0)
            {
                _logger.LogWarning("No IDynamicTool implementations found in {File}", fileName);
                return;
            }

            // Unload previous version if exists
            LoadedTools.TryRemove(fileName, out _);

            foreach (var toolType in toolTypes)
            {
                var instance = DynamicToolCompiler.CreateInstance(toolType);
                if (instance != null)
                {
                    LoadedTools[fileName] = (toolType, instance);
                    _logger.LogInformation("Loaded dynamic tool: {Name} ({Type}) from {File}",
                        instance.Name, toolType.Name, fileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tool file: {File}", fileName);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
            _disposed = true;
        }
    }
}
