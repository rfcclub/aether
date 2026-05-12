using Aether.Ui;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public sealed class CallbackRouterTests
{
    private static CallbackRouter CreateRouter(params IUiCallbackHandler[] handlers)
    {
        return new CallbackRouter(handlers, NullLogger<CallbackRouter>.Instance);
    }

    private class FakeHandler : IUiCallbackHandler
    {
        public string Namespace { get; }
        private readonly Func<UiCallback, IServiceProvider, string, Task<UiDocument?>> _handler;

        public FakeHandler(string ns, Func<UiCallback, IServiceProvider, string, Task<UiDocument?>> handler)
        {
            Namespace = ns;
            _handler = handler;
        }

        public Task<UiDocument?> HandleAsync(UiCallback callback, IServiceProvider services, string agentId)
            => _handler(callback, services, agentId);
    }

    [Fact]
    public async Task RouteAsync_DispatchesToCorrectHandler()
    {
        UiDocument? received = null;
        var handler = new FakeHandler("test", (cb, svc, agent) =>
        {
            received = new UiDocument { Text = $"Got: {cb.Action}" };
            return Task.FromResult<UiDocument?>(received);
        });

        var router = CreateRouter(handler);
        var callback = new UiCallback { Namespace = "test", Action = "ping" };

        var result = await router.RouteAsync(callback, null!, "");

        Assert.NotNull(result);
        Assert.Equal("Got: ping", result!.Text);
    }

    [Fact]
    public async Task RouteAsync_UnknownNamespace_ReturnsNull()
    {
        var handler = new FakeHandler("known", (_, _, _) => Task.FromResult<UiDocument?>(new UiDocument()));
        var router = CreateRouter(handler);

        var callback = new UiCallback { Namespace = "unknown", Action = "test" };
        var result = await router.RouteAsync(callback, null!, "");

        Assert.Null(result);
    }

    [Fact]
    public async Task RouteAsync_HandlerThrows_ReturnsNull()
    {
        var handler = new FakeHandler("crash", (_, _, _) =>
            throw new InvalidOperationException("boom"));

        var router = CreateRouter(handler);
        var callback = new UiCallback { Namespace = "crash", Action = "explode" };

        var result = await router.RouteAsync(callback, null!, "");

        Assert.Null(result); // should not throw
    }

    [Fact]
    public async Task RouteAsync_NamespaceCaseInsensitive()
    {
        var handler = new FakeHandler("Model", (cb, svc, agent) =>
            Task.FromResult<UiDocument?>(new UiDocument { Text = "ok" }));

        var router = CreateRouter(handler);
        var result = await router.RouteAsync(
            new UiCallback { Namespace = "model", Action = "test" }, null!, "");

        Assert.NotNull(result);
    }
}
