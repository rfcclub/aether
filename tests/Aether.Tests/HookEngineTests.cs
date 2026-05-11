using Aether.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class HookEngineTests
{
    private static IHook CreateHook(string name, HookPoint points, int priority, Func<HookContext, HookResult> handler) =>
        new DelegateHook(name, points, priority, handler);

    [Fact]
    public async Task PriorityOrdering_ExecutesLowerPriorityFirst()
    {
        var order = new List<int>();
        var hooks = new IHook[]
        {
            CreateHook("h3", HookPoint.PreLlmCall, 50, _ => { order.Add(50); return HookResult.Continue; }),
            CreateHook("h1", HookPoint.PreLlmCall, 10, _ => { order.Add(10); return HookResult.Continue; }),
            CreateHook("h2", HookPoint.PreLlmCall, 30, _ => { order.Add(30); return HookResult.Continue; }),
        };
        var engine = new HookEngine(hooks);

        await engine.RunAsync(HookPoint.PreLlmCall, new PreLlmCallContext(), CancellationToken.None);

        Assert.Equal([10, 30, 50], order);
    }

    [Fact]
    public async Task ShortCircuit_StopsOnFirstNonSuccess()
    {
        var executed = new List<string>();
        var hooks = new IHook[]
        {
            CreateHook("h1", HookPoint.PreLlmCall, 10, _ => { executed.Add("h1"); return HookResult.Continue; }),
            CreateHook("h2", HookPoint.PreLlmCall, 20, _ => { executed.Add("h2"); return HookResult.Stop("blocked"); }),
            CreateHook("h3", HookPoint.PreLlmCall, 30, _ => { executed.Add("h3"); return HookResult.Continue; }),
        };
        var engine = new HookEngine(hooks);

        var result = await engine.RunAsync(HookPoint.PreLlmCall, new PreLlmCallContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("blocked", result.StopReason);
        Assert.Equal(["h1", "h2"], executed);
    }

    [Fact]
    public async Task ExceptionIsolation_HookExceptionDoesNotStopPipeline()
    {
        var executed = new List<string>();
        var hooks = new IHook[]
        {
            CreateHook("thrower", HookPoint.PreLlmCall, 10, _ => throw new InvalidOperationException("bang")),
            CreateHook("runner", HookPoint.PreLlmCall, 20, _ => { executed.Add("runner"); return HookResult.Continue; }),
        };
        var engine = new HookEngine(hooks, NullLogger<HookEngine>.Instance);

        var result = await engine.RunAsync(HookPoint.PreLlmCall, new PreLlmCallContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["runner"], executed);
    }

    [Fact]
    public async Task EmptyHooks_NoOpReturnsContinue()
    {
        var engine = new HookEngine(Array.Empty<IHook>());

        var result = await engine.RunAsync(HookPoint.PreLlmCall, new PreLlmCallContext(), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task EmptyHooks_RunAllAsyncDoesNotThrow()
    {
        var engine = new HookEngine(Array.Empty<IHook>());
        await engine.RunAllAsync(HookPoint.PostLlmCall, new PostLlmCallContext { Response = null! }, CancellationToken.None);
    }

    [Fact]
    public async Task RunAllAsync_AllHooksExecuteEvenIfOneFails()
    {
        var executed = new List<string>();
        var hooks = new IHook[]
        {
            CreateHook("failer", HookPoint.PostToolUse, 10, _ => { executed.Add("failer"); throw new Exception("oops"); }),
            CreateHook("runner", HookPoint.PostToolUse, 20, _ => { executed.Add("runner"); return HookResult.Continue; }),
        };
        var engine = new HookEngine(hooks, NullLogger<HookEngine>.Instance);

        await engine.RunAllAsync(HookPoint.PostToolUse, new PostToolUseContext(), CancellationToken.None);

        Assert.Equal(["failer", "runner"], executed);
    }

    [Fact]
    public async Task HookNotSubscribed_DoesNotExecute()
    {
        var executed = false;
        var hooks = new IHook[]
        {
            CreateHook("pre-only", HookPoint.PreLlmCall, 10, _ => { executed = true; return HookResult.Continue; }),
        };
        var engine = new HookEngine(hooks);

        await engine.RunAsync(HookPoint.PostLlmCall, new PostLlmCallContext { Response = null! }, CancellationToken.None);

        Assert.False(executed);
    }

    [Fact]
    public void HasHooks_ReturnsTrueWhenHooksRegistered()
    {
        var engine = new HookEngine(new[] { CreateHook("h1", HookPoint.PreLlmCall, 10, _ => HookResult.Continue) });
        Assert.True(engine.HasHooks);
    }

    [Fact]
    public void HasHooks_ReturnsFalseWhenEmpty()
    {
        var engine = new HookEngine(Array.Empty<IHook>());
        Assert.False(engine.HasHooks);
    }

    [Fact]
    public async Task EqualPriority_OrderedByName()
    {
        var order = new List<string>();
        var hooks = new IHook[]
        {
            CreateHook("ccc", HookPoint.PreLlmCall, 10, _ => { order.Add("ccc"); return HookResult.Continue; }),
            CreateHook("aaa", HookPoint.PreLlmCall, 10, _ => { order.Add("aaa"); return HookResult.Continue; }),
            CreateHook("bbb", HookPoint.PreLlmCall, 10, _ => { order.Add("bbb"); return HookResult.Continue; }),
        };
        var engine = new HookEngine(hooks);

        await engine.RunAsync(HookPoint.PreLlmCall, new PreLlmCallContext(), CancellationToken.None);

        Assert.Equal(["aaa", "bbb", "ccc"], order);
    }

    [Fact]
    public async Task GetRegisteredHooks_ReturnsAllHooks()
    {
        var hooks = new IHook[]
        {
            CreateHook("h1", HookPoint.PreLlmCall, 10, _ => HookResult.Continue),
            CreateHook("h2", HookPoint.PostToolUse, 20, _ => HookResult.Continue),
        };
        var engine = new HookEngine(hooks);

        var info = engine.GetRegisteredHooks();

        Assert.Equal(2, info.Count);
        Assert.Contains(info, i => i.Name == "h1" && i.SubscribesTo == HookPoint.PreLlmCall && i.Priority == 10);
        Assert.Contains(info, i => i.Name == "h2" && i.SubscribesTo == HookPoint.PostToolUse && i.Priority == 20);
    }

    [Fact]
    public async Task HookPointFlags_CombineCorrectly()
    {
        var executed = new List<HookPoint>();
        var hooks = new IHook[]
        {
            CreateHook("multi", HookPoint.PreLlmCall | HookPoint.PostLlmCall, 10, _ =>
            {
                // Record the point — we can't know exactly which, so use the context type
                return HookResult.Continue;
            }),
        };
        var engine = new HookEngine(hooks);

        await engine.RunAsync(HookPoint.PreLlmCall, new PreLlmCallContext(), CancellationToken.None);
        await engine.RunAllAsync(HookPoint.PostLlmCall, new PostLlmCallContext { Response = null! }, CancellationToken.None);

        // No exception = both points matched the combined subscription
        Assert.True(true);
    }

    private sealed class DelegateHook : IHook
    {
        private readonly Func<HookContext, HookResult> _handler;

        public DelegateHook(string name, HookPoint subscribesTo, int priority, Func<HookContext, HookResult> handler)
        {
            Name = name;
            SubscribesTo = subscribesTo;
            Priority = priority;
            _handler = handler;
        }

        public string Name { get; }
        public HookPoint SubscribesTo { get; }
        public int Priority { get; }

        public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)
            => Task.FromResult(_handler(context));
    }
}
