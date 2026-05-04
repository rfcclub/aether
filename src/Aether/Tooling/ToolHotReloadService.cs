using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

/// <summary>
/// Background service that watches a configurable <c>tools/</c> directory for
/// <c>.json</c> tool definition files and registers/unregisters them in
/// <see cref="ToolRegistry"/> at runtime — no restart required.
///
/// A 2-second debounce timer prevents duplicate processing from
/// <see cref="FileSystemWatcher"/> firing multiple events for a single save.
/// </summary>
public sealed class ToolHotReloadService : BackgroundService
{
    private readonly ToolRegistry _registry;
    private readonly string _toolsPath;
    private readonly ILogger<ToolHotReloadService> _logger;

    // Debounce state is shared across all files: any file-system event resets the
    // timer and queues the changed/deleted file. When the timer fires, we scan all
    // .json files (for additions / modifications) and check deletions.
    private readonly HashSet<string> _pendingChanges = new();
    private readonly HashSet<string> _pendingDeletions = new();
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromSeconds(2);

    // Periodic sweep timer — runs DetectMissedDeletions independently of file events
    // to catch deletions that FileSystemWatcher may miss (common on WSL / network shares).
    private Timer? _sweepTimer;
    private readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(5);

    // Keep track of known files so we can map file names to tool names reliably.
    // key = file name (e.g., "my-tool.json"), value = tool name (from JSON "name" field).
    // FileSystemWatcher does not always fire a Deleted event on all platforms.
    private readonly Dictionary<string, string> _knownFiles = new(StringComparer.OrdinalIgnoreCase);

    public ToolHotReloadService(
        ToolRegistry registry,
        ILogger<ToolHotReloadService> logger,
        string toolsPath = "tools")
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _toolsPath = toolsPath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure the tools directory exists.
        Directory.CreateDirectory(_toolsPath);

        // Load any .json files that already exist at startup.
        await LoadExistingToolsAsync(stoppingToken);

        // Start watching for changes.
        using var watcher = new FileSystemWatcher(_toolsPath, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Created += OnToolFileChanged;
        watcher.Changed += OnToolFileChanged;
        watcher.Deleted += OnToolFileDeleted;
        watcher.Renamed += OnToolFileRenamed;
        watcher.Error += OnWatcherError;

        // Periodic sweep timer catches deletions that FileSystemWatcher may miss
        // on platforms where Deleted events are unreliable (WSL, network shares).
        _sweepTimer = new Timer(
            _ => DetectMissedDeletions(),
            null,
            _sweepInterval,
            _sweepInterval);

        // Keep the service running until cancellation is requested.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _debounceTimer?.Dispose();
        _sweepTimer?.Dispose();
    }

    private async Task LoadExistingToolsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_toolsPath))
            return;

        foreach (var filePath in Directory.GetFiles(_toolsPath, "*.json"))
        {
            if (ct.IsCancellationRequested)
                break;

            var fileName = Path.GetFileName(filePath);
            var tool = await TryParseToolDefinitionAsync(filePath, ct);
            if (tool is not null)
            {
                _registry.Register(tool.Name, tool);
                _knownFiles[fileName] = tool.Name;
                _logger.LogInformation("Hot-reload loaded existing tool '{ToolName}' from {FilePath}", tool.Name, filePath);
            }
        }
    }

    // ── FileSystemWatcher event handlers ──────────────────────────────────

    private void OnToolFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingChanges.Add(e.FullPath);
            // If it was previously marked for deletion, un-mark it.
            _pendingDeletions.Remove(e.FullPath);
            ResetDebounceTimer();
        }
    }

    private void OnToolFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingDeletions.Add(e.FullPath);
            _pendingChanges.Remove(e.FullPath);
            ResetDebounceTimer();
        }
    }

    private void OnToolFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            // Treat the old name as deleted and the new name as created/changed.
            _pendingDeletions.Add(e.OldFullPath);
            _pendingChanges.Add(e.FullPath);
            // Remove the old name from known files so that the deletion handler
            // in ProcessPendingChanges won't find it (nothing to unregister).
            var oldFileName = Path.GetFileName(e.OldFullPath);
            _knownFiles.Remove(oldFileName);
            ResetDebounceTimer();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "FileSystemWatcher error in tools directory '{ToolsPath}'", _toolsPath);
    }

    // ── Debounce logic ────────────────────────────────────────────────────

    private void ResetDebounceTimer()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(
            _ => ProcessPendingChanges(),
            null,
            _debounceDelay,
            Timeout.InfiniteTimeSpan);
    }

    private void ProcessPendingChanges()
    {
        HashSet<string> changes;
        HashSet<string> deletions;

        lock (_lock)
        {
            changes = new HashSet<string>(_pendingChanges, StringComparer.OrdinalIgnoreCase);
            deletions = new HashSet<string>(_pendingDeletions, StringComparer.OrdinalIgnoreCase);
            _pendingChanges.Clear();
            _pendingDeletions.Clear();
        }

        // Process deletions first so that a delete-then-create sequence works correctly.
        foreach (var deletedPath in deletions)
        {
            var fileName = Path.GetFileName(deletedPath);
            // Look up the tool name from our known-files mapping. This is important
            // because the JSON "name" field may differ from the filename convention.
            if (_knownFiles.TryGetValue(fileName, out var toolName))
            {
                _registry.Unregister(toolName);
                _knownFiles.Remove(fileName);
                _logger.LogInformation("Hot-reload unregistered tool '{ToolName}' (file deleted)", toolName);
            }
            else
            {
                // Fallback: derive tool name from the filename.
                var fallbackName = Path.GetFileNameWithoutExtension(fileName);
                _registry.Unregister(fallbackName);
                _logger.LogInformation("Hot-reload unregistered tool '{ToolName}' (file deleted, fallback name)", fallbackName);
            }
        }

        // Process additions / modifications.
        foreach (var changedPath in changes)
        {
            if (!File.Exists(changedPath))
                continue;

            var fileName = Path.GetFileName(changedPath);
            var tool = TryParseToolDefinitionAsync(changedPath).GetAwaiter().GetResult();
            if (tool is not null)
            {
                _registry.Register(tool.Name, tool);
                _knownFiles[fileName] = tool.Name;
                _logger.LogInformation("Hot-reload registered tool '{ToolName}' from {FilePath}", tool.Name, changedPath);
            }
            else
            {
                _logger.LogWarning("Hot-reload skipped invalid tool definition in {FilePath}", changedPath);
            }
        }

        // Detect deletions by comparing known files to the current directory listing.
        DetectMissedDeletions();
    }

    /// <summary>
    /// FileSystemWatcher does not always fire a Deleted event for every file removal
    /// (especially on network shares or WSL). As a fallback, compare our known-files
    /// dictionary against the actual directory contents and unregister anything gone.
    /// </summary>
    private void DetectMissedDeletions()
    {
        HashSet<string> currentFiles;
        try
        {
            var files = Directory.GetFiles(_toolsPath, "*.json");
            currentFiles = new HashSet<string>(files.Select(f => Path.GetFileName(f)!), StringComparer.OrdinalIgnoreCase);
        }
        catch (DirectoryNotFoundException)
        {
            // Tools directory was deleted; unregister everything.
            foreach (var toolName in _knownFiles.Values)
            {
                _registry.Unregister(toolName);
                _logger.LogInformation("Hot-reload unregistered tool '{ToolName}' (tools directory removed)", toolName);
            }
            _knownFiles.Clear();
            return;
        }

        var gone = _knownFiles.Keys
            .Where(k => !currentFiles.Contains(k))
            .ToList();

        foreach (var fileName in gone)
        {
            if (_knownFiles.TryGetValue(fileName, out var toolName))
            {
                _registry.Unregister(toolName);
                _knownFiles.Remove(fileName);
                _logger.LogInformation("Hot-reload unregistered tool '{ToolName}' (file missing, deletion detected)", toolName);
            }
        }
    }

    // ── JSON parsing ──────────────────────────────────────────────────────

    /// <summary>
    /// Tries to parse a tool definition from a JSON file. Returns <c>null</c>
    /// (and logs the error) on any parse failure — does not throw.
    /// </summary>
    private async Task<ToolDefinition?> TryParseToolDefinitionAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogError("Tool definition in '{FilePath}' is missing required 'name' field", filePath);
                return null;
            }

            var description = root.TryGetProperty("description", out var descProp)
                ? descProp.GetString() ?? ""
                : "";

            // parameters_json is expected to be a JSON string that embeds a JSON object.
            // If not present, use an empty object as default.
            JsonElement parametersSchema;
            if (root.TryGetProperty("parameters_json", out var paramsProp) && paramsProp.ValueKind == JsonValueKind.String)
            {
                var raw = paramsProp.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        parametersSchema = JsonSerializer.Deserialize<JsonElement>(raw);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Tool '{ToolName}' has invalid 'parameters_json' in {FilePath}", name, filePath);
                        parametersSchema = default;
                    }
                }
                else
                {
                    parametersSchema = default;
                }
            }
            else
            {
                parametersSchema = default;
            }

            // schema_json is expected to be a JSON string that embeds a JSON Schema.
            if (root.TryGetProperty("schema_json", out var schemaProp) && schemaProp.ValueKind == JsonValueKind.String)
            {
                var raw = schemaProp.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        parametersSchema = JsonSerializer.Deserialize<JsonElement>(raw);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Tool '{ToolName}' has invalid 'schema_json' in {FilePath}", name, filePath);
                    }
                }
            }

            // Hot-reloaded tools are passive: they return a notification that they
            // were invoked, since the actual implementation comes from code. If a
            // hot-reload tool is called, we log it and return a descriptive result.
            return new ToolDefinition(
                name,
                description,
                parametersSchema,
                async (args, ct) =>
                {
                    await Task.CompletedTask;
                    _logger.LogInformation(
                        "Hot-reloaded tool '{ToolName}' was invoked with args: {Args}",
                        name, args.ToString());
                    return $"Tool '{name}' executed (hot-reloaded — no code-behind). Args: {args}";
                });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse tool definition JSON in '{FilePath}'", filePath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error reading tool definition '{FilePath}'", filePath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied reading tool definition '{FilePath}'", filePath);
            return null;
        }
    }
}
