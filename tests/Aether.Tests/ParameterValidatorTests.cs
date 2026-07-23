using System.Text.Json;
using Aether.Providers;
using Aether.Tooling;

namespace Aether.Tests;

public class ParameterValidatorTests
{
    private static readonly LlmTool ReadTool = new(
        Name: "read",
        Description: "Read a file",
        ParametersJson: """{"type":"object"}""",
        SchemaJson: """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}""");

    private static readonly LlmTool BashTool = new(
        Name: "bash",
        Description: "Run a command",
        ParametersJson: """{"type":"object"}""",
        SchemaJson: """{"type":"object","properties":{"command":{"type":"string"},"cwd":{"type":"string"}},"required":["command"],"additionalProperties":false}""");

    private static readonly LlmTool NoSchemaTool = new(
        Name: "noop",
        Description: "No schema tool",
        ParametersJson: """{"type":"object"}""",
        SchemaJson: null);

    [Fact]
    public void Validate_ValidArguments_ReturnsEmpty()
    {
        var call = new LlmToolCall("call-1", "read",
            new Dictionary<string, string> { ["path"] = "/tmp/test.txt" });

        var errors = ParameterValidator.Validate(call, ReadTool);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingRequiredProperty_ReturnsError()
    {
        var call = new LlmToolCall("call-1", "read",
            new Dictionary<string, string>());

        var errors = ParameterValidator.Validate(call, ReadTool);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Path?.Contains("path") ?? false);
    }

    [Fact]
    public void Validate_AllPropertiesString_ValidatesOk()
    {
        // All tool call args are IReadOnlyDictionary<string, string>, so they're always strings.
        // Schema type validation still verifies required fields and no extra properties.
        var call = new LlmToolCall("call-1", "bash",
            new Dictionary<string, string> { ["command"] = "ls", ["cwd"] = "/tmp" });

        var errors = ParameterValidator.Validate(call, BashTool);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ExtraProperties_ReturnsError()
    {
        var call = new LlmToolCall("call-1", "read",
            new Dictionary<string, string> { ["path"] = "/tmp/test.txt", ["invalid"] = "extra" });

        var errors = ParameterValidator.Validate(call, ReadTool);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_MultipleErrors_AllReported()
    {
        var call = new LlmToolCall("call-1", "bash",
            new Dictionary<string, string> { ["extra"] = "bad" });

        var errors = ParameterValidator.Validate(call, BashTool);

        // Missing required "command" and extra property "extra"
        Assert.True(errors.Count >= 2);
    }

    [Fact]
    public void Validate_NoSchema_SkipsValidation()
    {
        var call = new LlmToolCall("call-1", "noop",
            new Dictionary<string, string> { ["anything"] = "goes" });

        var errors = ParameterValidator.Validate(call, NoSchemaTool);

        Assert.Empty(errors);
    }

    [Fact]
    public void FormatErrors_IncludesPathAndKind()
    {
        var call = new LlmToolCall("call-1", "read",
            new Dictionary<string, string>());

        var errors = ParameterValidator.Validate(call, ReadTool);
        var formatted = ParameterValidator.FormatErrors(errors);

        Assert.Contains("Tool validation failed:", formatted);
        Assert.Contains("#", formatted);
    }

    [Fact]
    public void SchemaCompilation_CachesCompiledSchema()
    {
        var call = new LlmToolCall("call-1", "read",
            new Dictionary<string, string> { ["path"] = "/tmp/test.txt" });

        // Call twice — second should use cached schema
        var errors1 = ParameterValidator.Validate(call, ReadTool);
        var errors2 = ParameterValidator.Validate(call, ReadTool);

        Assert.Empty(errors1);
        Assert.Empty(errors2);
    }

    [Fact]
    public void Validate_InvalidSchema_ThrowsOnCompilation()
    {
        var badTool = new LlmTool("bad", "desc", """{"type":"object"}""",
            SchemaJson: "not valid json schema at all");

        var call = new LlmToolCall("call-1", "bad",
            new Dictionary<string, string>());

        // NJsonSchema throws when parsing invalid schema — wrapped in AggregateException by .Result
        var ex = Assert.Throws<AggregateException>(() => ParameterValidator.Validate(call, badTool));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void FormatErrors_MultipleErrors_EachOnNewLine()
    {
        var call = new LlmToolCall("call-1", "bash",
            new Dictionary<string, string> { ["extra"] = "bad" });

        var errors = ParameterValidator.Validate(call, BashTool);
        var formatted = ParameterValidator.FormatErrors(errors);

        var lines = formatted.Split(Environment.NewLine);
        Assert.True(lines.Length >= 3); // Header + at least 2 errors
    }

    [Fact]
    public void Validate_EmptySchema_ReturnsNoErrors()
    {
        var tool = new LlmTool("empty-schema", "desc", """{"type":"object"}""",
            SchemaJson: "");
        var call = new LlmToolCall("call-1", "empty-schema",
            new Dictionary<string, string> { ["any"] = "thing" });

        var errors = ParameterValidator.Validate(call, tool);

        Assert.Empty(errors);
    }
}
