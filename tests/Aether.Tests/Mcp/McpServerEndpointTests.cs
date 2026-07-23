using System.Text.Json;
using Aether.Mcp;
using Aether.Tooling;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests.Mcp;

public class McpServerEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static McpServerEndpoint CreateEndpoint(params (string, string)[] tools)
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        foreach (var (name, desc) in tools)
        {
            var schema = JsonSerializer.Deserialize<JsonElement>("{}");
            registry.Register(name, new ToolDefinition(name, desc, schema,
                (args, ct) => Task.FromResult<object>($"executed {name}"), ToolRisk.Read));
        }
        return new McpServerEndpoint(registry, NullLogger<McpServerEndpoint>.Instance);
    }

    private static string Serialize(object obj)
        => JsonSerializer.Serialize(obj, JsonOpts);

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOpts)!;

    private static JsonRpcMessage MakeRequest(string method, string? id = "1", object? args = null)
    {
        var msg = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (args != null) msg["params"] = args;
        return Deserialize<JsonRpcMessage>(Serialize(msg));
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var endpoint = CreateEndpoint(("test", "a tool"));
        var request = MakeRequest("initialize");

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.Equal("2.0", response.Jsonrpc);
        Assert.Equal("1", response.Id);
        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
        var text = response.Result.Value.GetRawText();
        Assert.Contains("aether", text);
        Assert.Contains("3.0.1", text);
    }

    [Fact]
    public async Task ToolsList_ReturnsRegisteredTools()
    {
        var endpoint = CreateEndpoint(("read", "Reads files"), ("write", "Writes files"));
        var request = MakeRequest("tools/list");

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
        var tools = response.Result.Value.Deserialize<Dictionary<string, JsonElement>>()["tools"]
            .Deserialize<List<Dictionary<string, JsonElement>>>();
        Assert.Contains(tools, t => t["name"].GetString() == "read");
        Assert.Contains(tools, t => t["name"].GetString() == "write");
    }

    [Fact]
    public async Task UnknownMethod_ReturnsError()
    {
        var endpoint = CreateEndpoint();
        var request = MakeRequest("bogus_method");

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.NotNull(response.Error);
        Assert.Equal(-32601, response.Error.Code);
        Assert.Contains("bogus_method", response.Error.Message);
    }

    [Fact]
    public async Task MalformedJson_ReturnsParseError()
    {
        var endpoint = CreateEndpoint();

        var response = await endpoint.HandleRequestAsync("not valid json", CancellationToken.None);

        Assert.NotNull(response.Error);
        Assert.Equal(-32700, response.Error.Code);
    }

    [Fact]
    public async Task ToolCall_UnknownTool_ReturnsError()
    {
        var endpoint = CreateEndpoint();
        var request = MakeRequest("tools/call", args: new { name = "no_such_tool" });

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.NotNull(response.Error);
        Assert.Equal(-32602, response.Error.Code);
        Assert.Contains("no_such_tool", response.Error.Message);
    }

    [Fact]
    public async Task ToolCall_MissingParams_ReturnsError()
    {
        var endpoint = CreateEndpoint();
        var request = MakeRequest("tools/call");

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.NotNull(response.Error);
        Assert.Equal(-32602, response.Error.Code);
    }

    [Fact]
    public async Task ToolsCall_ExistingTool_ExecutesSuccessfully()
    {
        var endpoint = CreateEndpoint(("echo", "Echoes input"));
        var request = MakeRequest("tools/call", args: new { name = "echo", arguments = new { message = "hi" } });

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
        var resultStr = response.Result.Value.GetRawText();
        Assert.Contains("executed echo", resultStr);
    }

    [Fact]
    public async Task Notifications_Initialized_ReturnsEmpty()
    {
        var endpoint = CreateEndpoint();
        var request = MakeRequest("notifications/initialized");

        var response = await endpoint.HandleRequestAsync(Serialize(request), CancellationToken.None);

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
    }
}
