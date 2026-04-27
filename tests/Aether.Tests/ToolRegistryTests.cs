using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

public class ToolRegistryTests
{
    private readonly ToolRegistry _registry = new(NullLogger<ToolRegistry>.Instance);

    [Fact]
    public void Register_AddsTool()
    {
        _registry.Register("echo", new ToolDefinition("echo", "Echo back", default, (_, _) => Task.FromResult<object>("echo")));
        Assert.True(_registry.HasTool("echo"));
        Assert.NotNull(_registry.Resolve("echo"));
    }

    [Fact]
    public void Register_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _registry.Register("", new ToolDefinition("", "", default, (_, _) => Task.FromResult<object>(""))));
    }

    [Fact]
    public void Unregister_RemovesTool()
    {
        _registry.Register("test", new ToolDefinition("test", "", default, (_, _) => Task.FromResult<object>("")));
        _registry.Unregister("test");
        Assert.False(_registry.HasTool("test"));
    }

    [Fact]
    public void Resolve_Missing_ReturnsNull()
    {
        Assert.Null(_registry.Resolve("nonexistent"));
    }

    [Fact]
    public void List_ReturnsSortedNames()
    {
        _registry.Register("b", new ToolDefinition("b", "", default, (_, _) => Task.FromResult<object>("")));
        _registry.Register("a", new ToolDefinition("a", "", default, (_, _) => Task.FromResult<object>("")));

        var list = _registry.List().ToList();
        Assert.Equal(new[] { "a", "b" }, list);
    }
}
