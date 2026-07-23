using Aether.Tooling.DynamicTool;

namespace SampleTools;

/// <summary>
/// A minimal dynamic tool that returns a greeting.
/// Demonstrates the IDynamicTool interface with no dependencies.
/// </summary>
public class HelloWorldTool : IDynamicTool
{
    public string Name => "hello_world";

    public string Description => "Returns a friendly greeting. Use this tool to test dynamic tool loading.";

    public string ParameterSchemaJson => @"{
        ""type"": ""object"",
        ""properties"": {
            ""name"": {
                ""type"": ""string"",
                ""description"": ""Optional name to greet""
            }
        },
        ""additionalProperties"": false
    }";

    public Task<string> ExecuteAsync(Dictionary<string, object> args)
    {
        var name = args.TryGetValue("name", out var n) ? n?.ToString() : null;
        var greeting = string.IsNullOrEmpty(name)
            ? "Hello from DynamicTool! 👋"
            : $"Hello, {name}! Welcome to DynamicTool. 👋";

        return Task.FromResult(greeting);
    }
}
