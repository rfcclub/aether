using System.Globalization;
using Aether.Tooling.DynamicTool;

namespace SampleTools;

/// <summary>
/// A calculator tool that supports basic arithmetic operations.
/// Demonstrates more complex parameter handling.
/// </summary>
public class CalculatorTool : IDynamicTool
{
    public string Name => "calculator";

    public string Description => "Performs basic arithmetic operations (add, subtract, multiply, divide).";

    public string ParameterSchemaJson => @"{
        ""type"": ""object"",
        ""properties"": {
            ""a"": {
                ""type"": ""number"",
                ""description"": ""First operand""
            },
            ""b"": {
                ""type"": ""number"",
                ""description"": ""Second operand""
            },
            ""op"": {
                ""type"": ""string"",
                ""enum"": [""add"", ""subtract"", ""multiply"", ""divide""],
                ""description"": ""Operation to perform""
            }
        },
        ""required"": [""a"", ""b"", ""op""],
        ""additionalProperties"": false
    }";

    public Task<string> ExecuteAsync(Dictionary<string, object> args)
    {
        if (!args.TryGetValue("a", out var aVal) || !TryGetDouble(aVal, out var a))
            return Task.FromResult("Error: 'a' must be a valid number");

        if (!args.TryGetValue("b", out var bVal) || !TryGetDouble(bVal, out var b))
            return Task.FromResult("Error: 'b' must be a valid number");

        var op = args.TryGetValue("op", out var opVal) ? opVal?.ToString()?.ToLowerInvariant() : null;
        if (string.IsNullOrEmpty(op))
            return Task.FromResult("Error: 'op' must be one of: add, subtract, multiply, divide");

        var result = op switch
        {
            "add" => a + b,
            "subtract" => a - b,
            "multiply" => a * b,
            "divide" => b == 0 ? throw new InvalidOperationException("Division by zero") : a / b,
            _ => throw new ArgumentException($"Unsupported operation: {op}")
        };

        return Task.FromResult($"{FormatNumber(a)} {GetOpSymbol(op)} {FormatNumber(b)} = {FormatNumber(result)}");
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        if (value is double d)
        {
            result = d;
            return true;
        }
        if (value is int i)
        {
            result = i;
            return true;
        }
        if (value is long l)
        {
            result = l;
            return true;
        }
        if (value is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }
        result = 0;
        return false;
    }

    private static string FormatNumber(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("G", CultureInfo.InvariantCulture);

    private static string GetOpSymbol(string op) => op switch
    {
        "add" => "+",
        "subtract" => "-",
        "multiply" => "×",
        "divide" => "÷",
        _ => "?"
    };
}
