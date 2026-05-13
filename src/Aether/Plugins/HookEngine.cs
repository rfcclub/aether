using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Plugins;

public class HookEngine
{
    private readonly IReadOnlyList<IHook> _hooks;
    private readonly ILogger<HookEngine> _logger;
    private static readonly TimeSpan SlowHookThreshold = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan KillHookThreshold = TimeSpan.FromMilliseconds(5000);

    public HookEngine(IEnumerable<IHook> hooks, ILogger<HookEngine>? logger = null)
    {
        _hooks = hooks
            .OrderBy(h => h.Priority)
            .ThenBy(h => h.Name)
            .ToList();
        _logger = logger ?? NullLogger<HookEngine>.Instance;
    }

    public HookEngine(IEnumerable<IHook> hooks) : this(hooks, null) { }

    public bool HasHooks => _hooks.Count > 0;

    public HookEngine FilterForAgent(AgentPluginConfig config)
    {
        var enabled = new HashSet<string>(config.Enabled, StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(config.Disabled, StringComparer.OrdinalIgnoreCase);

        var hooks = _hooks.Where(h =>
            enabled.Count > 0
                ? enabled.Contains(h.Name)
                : !disabled.Contains(h.Name));

        if (config.HookOverrides.Count > 0)
        {
            hooks = hooks.Select(h => config.HookOverrides.TryGetValue(h.Name, out var priority)
                ? new PriorityOverrideHook(h, priority)
                : h);
        }

        return new HookEngine(hooks, _logger);
    }

    /// <summary>
    /// Execute all hooks subscribed to the given point in priority order.
    /// Stops on first non-success result (short-circuit).
    /// Returns the first non-success, or Continue.
    /// </summary>
    public async Task<HookResult> RunAsync(HookPoint point, HookContext context, CancellationToken ct)
    {
        if (_hooks.Count == 0) return HookResult.Continue;

        foreach (var hook in _hooks)
        {
            if ((hook.SubscribesTo & point) == 0) continue;
            ct.ThrowIfCancellationRequested();

            try
            {
                var sw = Stopwatch.StartNew();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(KillHookThreshold);

                var result = await hook.ExecuteAsync(context, timeoutCts.Token);
                sw.Stop();

                if (sw.Elapsed > SlowHookThreshold)
                {
                    _logger.LogWarning("Slow hook: {HookName} at {Point} took {Ms}ms",
                        hook.Name, point, (long)sw.Elapsed.TotalMilliseconds);
                }

                if (!result.Success)
                {
                    _logger.LogInformation("Hook {HookName} stopped pipeline at {Point}: {Reason}",
                        hook.Name, point, result.StopReason);
                    return result;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogError("Hook {HookName} timed out at {Point} after {Ms}ms — skipping",
                    hook.Name, point, (long)KillHookThreshold.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook {HookName} threw at {Point} — continuing pipeline",
                    hook.Name, point);
            }
        }

        return HookResult.Continue;
    }

    /// <summary>
    /// Run all hooks subscribed to the given point. All hooks execute regardless
    /// of individual results. Used for Post* and fire-and-forget hooks.
    /// </summary>
    public async Task RunAllAsync(HookPoint point, HookContext context, CancellationToken ct)
    {
        if (_hooks.Count == 0) return;

        foreach (var hook in _hooks)
        {
            if ((hook.SubscribesTo & point) == 0) continue;
            ct.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(KillHookThreshold);

                await hook.ExecuteAsync(context, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogError("Hook {HookName} timed out at {Point}", hook.Name, point);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook {HookName} threw at {Point}", hook.Name, point);
            }
        }
    }

    public IReadOnlyList<HookInfo> GetRegisteredHooks()
        => _hooks.Select(h => new HookInfo(h.Name, h.SubscribesTo, h.Priority)).ToList();
}

public record HookInfo(string Name, HookPoint SubscribesTo, int Priority);

internal sealed class PriorityOverrideHook : IHook
{
    private readonly IHook _inner;

    public PriorityOverrideHook(IHook inner, int priority)
    {
        _inner = inner;
        Priority = priority;
    }

    public string Name => _inner.Name;
    public HookPoint SubscribesTo => _inner.SubscribesTo;
    public int Priority { get; }
    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
        => _inner.ExecuteAsync(context, ct);
}
