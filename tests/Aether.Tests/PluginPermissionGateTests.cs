using Aether.Plugins;

namespace Aether.Tests;

public class PluginPermissionGateTests
{
    [Fact]
    public void DeclaredService_Resolved()
    {
        var inner = new SimpleServiceProvider(new Dictionary<Type, object>
        {
            [typeof(DummyDb)] = new DummyDb()
        });
        var manifest = new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            Permissions = new PluginPermissions { Services = new() { "DummyDb" } }
        };
        var gate = new PluginPermissionGate(inner, manifest);

        var result = gate.GetService(typeof(DummyDb));

        Assert.NotNull(result);
        Assert.IsType<DummyDb>(result);
    }

    [Fact]
    public void UndeclaredService_ReturnsNull()
    {
        var inner = new SimpleServiceProvider(new Dictionary<Type, object>
        {
            [typeof(DummyDb)] = new DummyDb()
        });
        var manifest = new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            Permissions = new PluginPermissions { Services = new() { "OtherService" } }
        };
        var gate = new PluginPermissionGate(inner, manifest);

        var result = gate.GetService(typeof(DummyDb));

        Assert.Null(result);
    }

    [Fact]
    public void NoPermissionsDeclared_ServiceReturnsNull()
    {
        var inner = new SimpleServiceProvider(new Dictionary<Type, object>
        {
            [typeof(DummyDb)] = new DummyDb()
        });
        var manifest = new PluginManifest { Name = "test-plugin", Version = "1.0.0" };
        var gate = new PluginPermissionGate(inner, manifest);

        var result = gate.GetService(typeof(DummyDb));

        Assert.Null(result);
    }

    [Fact]
    public void DefaultPermissions_NoServicesAllowed()
    {
        var inner = new SimpleServiceProvider(new Dictionary<Type, object>
        {
            [typeof(DummyDb)] = new DummyDb()
        });
        var manifest = new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            Permissions = PluginPermissions.Default
        };
        var gate = new PluginPermissionGate(inner, manifest);

        var result = gate.GetService(typeof(DummyDb));
        Assert.Null(result);
    }

    private sealed class DummyDb { }

    private sealed class SimpleServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;

        public SimpleServiceProvider(Dictionary<Type, object> services)
        {
            _services = services;
        }

        public object? GetService(Type serviceType)
        {
            _services.TryGetValue(serviceType, out var svc);
            return svc;
        }
    }
}
