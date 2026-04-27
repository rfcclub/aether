using System.Collections.Concurrent;
using System.Text.Json;
using Aether.Providers;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Aether.Tooling;

public static class ParameterValidator
{
    private static readonly ConcurrentDictionary<string, JsonSchema> CompiledSchemas = new();

    public static ICollection<ValidationError> Validate(LlmToolCall call, LlmTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.SchemaJson))
            return Array.Empty<ValidationError>();

        var schema = CompiledSchemas.GetOrAdd(tool.Name, _ =>
        {
            var s = JsonSchema.FromJsonAsync(tool.SchemaJson).Result;
            return s;
        });

        var argsJson = JsonSerializer.Serialize(call.Arguments);
        return schema.Validate(argsJson);
    }

    public static string FormatErrors(ICollection<ValidationError> errors)
    {
        var lines = new List<string> { "Tool validation failed:" };
        foreach (var error in errors)
        {
            lines.Add($"- {error.Path}: {error.Kind}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
