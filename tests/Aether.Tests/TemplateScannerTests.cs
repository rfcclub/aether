using Aether.Providers;

namespace Aether.Tests;

/// <summary>
/// Tests for TemplateScanner — parses ~/.anima/providers.d/*.yaml templates.
/// Mirrors scenarios from openspec/changes/provider-onboarding-preset-raw/specs.
/// </summary>
public sealed class TemplateScannerTests : IDisposable
{
    private readonly string _tempDir;

    public TemplateScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_ts_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // --- MapApiToType ---

    [Theory]
    [InlineData("openai-completions", "openai")]
    [InlineData("anthropic-messages", "anthropic")]
    public void MapApiToType_maps_supported_adapters(string api, string expected)
    {
        Assert.Equal(expected, TemplateScanner.MapApiToType(api));
    }

    [Theory]
    [InlineData("generic", null)]
    [InlineData("unknown-api", null)]
    [InlineData("", null)]
    public void MapApiToType_returns_null_for_unsupported(string api, string? expected)
    {
        Assert.Equal(expected, TemplateScanner.MapApiToType(api));
    }

    [Fact]
    public void MapApiToType_is_case_insensitive()
    {
        Assert.Equal("openai", TemplateScanner.MapApiToType("OpenAI-Completions"));
        Assert.Equal("anthropic", TemplateScanner.MapApiToType("ANTHROPIC-MESSAGES"));
    }

    // --- ParseTemplateFile ---

    [Fact]
    public void ParseTemplateFile_parses_valid_nahcrof_format()
    {
        var path = WriteYaml("nahcrof.yaml", """
            id: nahcrof
            label: "Nahcrof AI"
            api: openai-completions
            baseUrl: https://crof.ai/v1
            apiKey: ${NAHCROF_API_KEY}
            models:
              - id: glm-4.7-flash
              - id: glm-5.2
              - id: deepseek-v4-pro
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Equal("nahcrof", t!.Id);
        Assert.Equal("Nahcrof AI", t.Label);
        Assert.Equal("openai-completions", t.Api);
        Assert.Equal("https://crof.ai/v1", t.BaseUrl);
        Assert.Equal("${NAHCROF_API_KEY}", t.ApiKeyRef);
        Assert.Equal(new[] { "glm-4.7-flash", "glm-5.2", "deepseek-v4-pro" }, t.Models.ToArray());
        Assert.True(t.Supported);
        Assert.Equal("openai", t.MappedType);
    }

    [Fact]
    public void ParseTemplateFile_strips_single_quotes_from_label()
    {
        var path = WriteYaml("q.yaml", """
            id: q
            label: 'Quoted Single'
            api: openai-completions
            baseUrl: https://q.example/v1
            apiKey: lit-key
            models:
              - id: m1
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Equal("Quoted Single", t!.Label);
        Assert.Equal("lit-key", t.ApiKeyRef);
    }

    [Fact]
    public void ParseTemplateFile_parses_unquoted_label()
    {
        var path = WriteYaml("g.yaml", """
            id: gemini
            label: Gemini
            baseUrl: https://generativelanguage.googleapis.com
            api: generic
            apiKey: AIzaSyLiteral
            models:
              - id: gemini-3.1-flash-lite-preview
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Equal("gemini", t!.Id);
        Assert.Equal("Gemini", t.Label);
        Assert.Equal("generic", t.Api);
        Assert.False(t.Supported);
        Assert.Null(t.MappedType);
        Assert.Equal("AIzaSyLiteral", t.ApiKeyRef);
    }

    [Fact]
    public void ParseTemplateFile_is_field_order_independent()
    {
        // gemini.yaml has baseUrl before api — parser must not assume order
        var path = WriteYaml("ord.yaml", """
            id: ord
            baseUrl: https://ord.example/v1
            api: anthropic-messages
            label: "Ord Test"
            apiKey: ${ORD_KEY}
            models:
              - id: m-a
              - id: m-b
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Equal("anthropic", t!.MappedType);
        Assert.Equal("https://ord.example/v1", t.BaseUrl);
        Assert.Equal("${ORD_KEY}", t.ApiKeyRef);
        Assert.Equal(new[] { "m-a", "m-b" }, t.Models.ToArray());
    }

    [Fact]
    public void ParseTemplateFile_returns_null_when_required_field_missing()
    {
        // missing baseUrl
        var path = WriteYaml("bad.yaml", """
            id: bad
            label: "Bad"
            api: openai-completions
            apiKey: key
            models:
              - id: m1
            """);

        Assert.Null(TemplateScanner.ParseTemplateFile(path));
    }

    [Fact]
    public void ParseTemplateFile_returns_null_when_id_missing()
    {
        var path = WriteYaml("noid.yaml", """
            label: "No Id"
            api: openai-completions
            baseUrl: https://x.example/v1
            apiKey: key
            models:
              - id: m1
            """);

        Assert.Null(TemplateScanner.ParseTemplateFile(path));
    }

    [Fact]
    public void ParseTemplateFile_returns_null_when_api_missing()
    {
        var path = WriteYaml("noapi.yaml", """
            id: noapi
            label: "No Api"
            baseUrl: https://x.example/v1
            apiKey: key
            models:
              - id: m1
            """);

        Assert.Null(TemplateScanner.ParseTemplateFile(path));
    }

    [Fact]
    public void ParseTemplateFile_handles_empty_models_as_empty_list()
    {
        var path = WriteYaml("nomodels.yaml", """
            id: nomodels
            label: "No Models"
            api: openai-completions
            baseUrl: https://x.example/v1
            apiKey: key
            models: []
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Empty(t!.Models);
    }

    [Fact]
    public void ParseTemplateFile_returns_null_on_malformed_yaml()
    {
        // garbage that doesn't look like the expected subset
        var path = WriteYaml("garbage.yaml", "this is not yaml\n  - broken: : :\n    ???\n");
        Assert.Null(TemplateScanner.ParseTemplateFile(path));
    }

    [Fact]
    public void ParseTemplateFile_handles_oauth_apikey_ref()
    {
        var path = WriteYaml("oauth.yaml", """
            id: openai
            label: "OpenAI"
            api: openai-completions
            baseUrl: https://api.openai.com/v1
            apiKey: ${OAUTH:openai}
            models:
              - id: gpt-5.5
              - id: gpt-5.4
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Equal("${OAUTH:openai}", t!.ApiKeyRef);
        Assert.True(t.Supported);
    }

    [Fact]
    public void ParseTemplateFile_handles_default_value_pattern_in_apikey()
    {
        var path = WriteYaml("def.yaml", """
            id: router9
            label: "9router"
            api: openai-completions
            baseUrl: http://127.0.0.1:20128/v1
            apiKey: ${NINE_ROUTER_KEY:-none}
            models:
              - id: aether
            """);

        var t = TemplateScanner.ParseTemplateFile(path);

        Assert.NotNull(t);
        Assert.Equal("${NINE_ROUTER_KEY:-none}", t!.ApiKeyRef);
    }

    // --- ScanTemplates ---

    [Fact]
    public void ScanTemplates_returns_array_for_dir_with_multiple_yaml()
    {
        WriteYaml("a.yaml", """
            id: a
            label: "A"
            api: openai-completions
            baseUrl: https://a.example/v1
            apiKey: ${A_KEY}
            models:
              - id: m1
            """);
        WriteYaml("b.yaml", """
            id: b
            label: "B"
            api: anthropic-messages
            baseUrl: https://b.example
            apiKey: ${B_KEY}
            models:
              - id: m2
            """);

        var templates = TemplateScanner.ScanTemplates(_tempDir);

        Assert.Equal(2, templates.Count);
        Assert.Contains(templates, t => t.Id == "a");
        Assert.Contains(templates, t => t.Id == "b");
    }

    [Fact]
    public void ScanTemplates_returns_empty_for_missing_directory()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        var templates = TemplateScanner.ScanTemplates(missing);
        Assert.NotNull(templates);
        Assert.Empty(templates);
    }

    [Fact]
    public void ScanTemplates_returns_empty_for_empty_directory()
    {
        var empty = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(empty);
        var templates = TemplateScanner.ScanTemplates(empty);
        Assert.Empty(templates);
    }

    [Fact]
    public void ScanTemplates_skips_non_yaml_files()
    {
        WriteYaml("valid.yaml", """
            id: valid
            label: "Valid"
            api: openai-completions
            baseUrl: https://v.example/v1
            apiKey: k
            models:
              - id: m1
            """);
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "not yaml");
        File.WriteAllText(Path.Combine(_tempDir, "backup.bak"), "id: x\n");

        var templates = TemplateScanner.ScanTemplates(_tempDir);

        Assert.Single(templates);
        Assert.Equal("valid", templates[0].Id);
    }

    [Fact]
    public void ScanTemplates_skips_malformed_files_silently()
    {
        WriteYaml("good.yaml", """
            id: good
            label: "Good"
            api: openai-completions
            baseUrl: https://g.example/v1
            apiKey: k
            models:
              - id: m1
            """);
        WriteYaml("bad.yaml", "garbage: : : broken\n  ??\n");

        var templates = TemplateScanner.ScanTemplates(_tempDir);

        Assert.Single(templates);
        Assert.Equal("good", templates[0].Id);
    }

    private string WriteYaml(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
