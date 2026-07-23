using Aether.Cli;
using Aether.Providers;

namespace Aether.Tests;

/// <summary>
/// Tests for the testable logic of the 3-mode onboarding wizard (OnboardingFlow).
/// The Spectre.Console prompts themselves are not tested here (they need IAnsiConsole);
/// only the pure logic — mode resolution, template selection, entry building — is covered.
/// </summary>
public sealed class OnboardingFlowTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _providersDir;
    private readonly string _animaEnvPath;

    public OnboardingFlowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aether_onb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _providersDir = Path.Combine(_tempDir, "providers.d");
        Directory.CreateDirectory(_providersDir);
        _animaEnvPath = Path.Combine(_tempDir, "anima.env");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteTemplate(string name, string content)
        => File.WriteAllText(Path.Combine(_providersDir, name), content);

    // --- ListImportableTemplates ---

    [Fact]
    public void ListImportableTemplates_returns_supported_templates_with_key_status()
    {
        WriteTemplate("testprov.yaml", """
            id: testprov
            label: "Test Provider"
            api: openai-completions
            baseUrl: https://testprov.example/v1
            apiKey: ${TESTPROV_UNIQ_KEY_8a3f}
            models:
              - id: glm-5.2
            """);
        File.WriteAllText(_animaEnvPath, "TESTPROV_UNIQ_KEY_8a3f=found-key\n");
        WriteTemplate("gemini.yaml", """
            id: gemini
            label: Gemini
            api: generic
            baseUrl: https://generativelanguage.googleapis.com
            apiKey: lit
            models:
              - id: gemini-3.1-flash
            """);

        var rows = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);

        Assert.Equal(2, rows.Count);
        var testprov = rows.Single(r => r.Id == "testprov");
        Assert.True(testprov.Supported);
        Assert.Equal(KeyStatus.Found, testprov.KeyStatus);
        var gemini = rows.Single(r => r.Id == "gemini");
        Assert.False(gemini.Supported);
        Assert.Equal(KeyStatus.Unsupported, gemini.KeyStatus);
    }

    [Fact]
    public void ListImportableTemplates_marks_missing_and_oauth()
    {
        WriteTemplate("missing.yaml", """
            id: missing
            label: "Missing"
            api: openai-completions
            baseUrl: https://missing.example/v1
            apiKey: ${MISSINGPROV_UNIQ_KEY_2d7c}
            models:
              - id: m1
            """);
        WriteTemplate("oauth.yaml", """
            id: openai
            label: "OpenAI"
            api: openai-completions
            baseUrl: https://api.openai.com/v1
            apiKey: ${OAUTH:openai}
            models:
              - id: gpt-5.5
            """);
        File.WriteAllText(_animaEnvPath, "");

        var rows = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);

        Assert.Equal(KeyStatus.Missing, rows.Single(r => r.Id == "missing").KeyStatus);
        Assert.Equal(KeyStatus.OAuth, rows.Single(r => r.Id == "openai").KeyStatus);
    }

    [Fact]
    public void ListImportableTemplates_empty_when_no_dir()
    {
        var rows = OnboardingFlow.ListImportableTemplates(
            Path.Combine(_tempDir, "nope"), _animaEnvPath);
        Assert.Empty(rows);
    }

    // --- ResolveModeFromChoice ---

    [Fact]
    public void ResolveModeFromChoice_maps_import()
    {
        Assert.Equal(OnboardingMode.Import,
            OnboardingFlow.ResolveModeFromChoice("Import from providers.d"));
    }

    [Fact]
    public void ResolveModeFromChoice_maps_raw()
    {
        Assert.Equal(OnboardingMode.Raw,
            OnboardingFlow.ResolveModeFromChoice("Custom Setup (Raw)"));
    }

    [Fact]
    public void ResolveModeFromChoice_maps_oauth()
    {
        Assert.Equal(OnboardingMode.OAuth,
            OnboardingFlow.ResolveModeFromChoice("OAuth Login"));
    }

    [Fact]
    public void ResolveModeFromChoice_defaults_to_import()
    {
        Assert.Equal(OnboardingMode.Import,
            OnboardingFlow.ResolveModeFromChoice(""));
        Assert.Equal(OnboardingMode.Import,
            OnboardingFlow.ResolveModeFromChoice("garbage"));
    }

    // --- ResolveTemplateSelection ---

    [Fact]
    public void ResolveTemplateSelection_finds_template_by_id()
    {
        WriteTemplate("testprov.yaml", """
            id: testprov
            label: "Test Provider"
            api: openai-completions
            baseUrl: https://testprov.example/v1
            apiKey: ${TESTPROV_UNIQ_KEY_8a3f}
            models:
              - id: glm-5.2
            """);
        File.WriteAllText(_animaEnvPath, "TESTPROV_UNIQ_KEY_8a3f=real-key\n");

        var templates = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);
        var selected = OnboardingFlow.ResolveTemplateSelection(templates, "testprov");

        Assert.NotNull(selected);
        Assert.Equal("testprov", selected!.Id);
        Assert.Equal(KeyStatus.Found, selected.KeyStatus);
    }

    [Fact]
    public void ResolveTemplateSelection_case_insensitive()
    {
        WriteTemplate("testprov.yaml", """
            id: testprov
            label: "Test"
            api: openai-completions
            baseUrl: https://x.example/v1
            apiKey: lit
            models:
              - id: m1
            """);
        File.WriteAllText(_animaEnvPath, "");

        var templates = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);
        var selected = OnboardingFlow.ResolveTemplateSelection(templates, "TESTPROV");

        Assert.NotNull(selected);
        Assert.Equal("testprov", selected!.Id);
    }

    [Fact]
    public void ResolveTemplateSelection_returns_null_when_not_found()
    {
        var templates = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);
        Assert.Null(OnboardingFlow.ResolveTemplateSelection(templates, "nope"));
    }

    // --- BuildRawTemplate ---

    [Fact]
    public void BuildRawTemplate_builds_openai_template_from_raw_inputs()
    {
        var template = OnboardingFlow.BuildRawTemplate(
            name: "custom-prov",
            url: "https://custom.example/v1",
            protocol: RawProtocol.OpenAiChat,
            apiKey: "sk-raw",
            models: new[] { "gpt-4o", "gpt-4o-mini" });

        Assert.Equal("custom-prov", template.Id);
        Assert.Equal("https://custom.example/v1", template.BaseUrl);
        Assert.Equal("openai", template.MappedType);
        Assert.Equal("sk-raw", template.ApiKeyRef);
        Assert.Equal(new[] { "gpt-4o", "gpt-4o-mini" }, template.Models.ToArray());
        Assert.True(template.Supported);
    }

    [Fact]
    public void BuildRawTemplate_builds_anthropic_template_for_anthropic_messages_protocol()
    {
        var template = OnboardingFlow.BuildRawTemplate(
            name: "mm",
            url: "https://api.minimax.io/anthropic",
            protocol: RawProtocol.AnthropicMessages,
            apiKey: "mm-key",
            models: new[] { "MiniMax-M2.7" });

        Assert.Equal("anthropic", template.MappedType);
        Assert.Equal("anthropic-messages", template.Api);
    }

    [Fact]
    public void BuildRawTemplate_splits_comma_separated_models_string()
    {
        var template = OnboardingFlow.BuildRawTemplate(
            name: "x",
            url: "https://x.example/v1",
            protocol: RawProtocol.OpenAiChat,
            apiKey: "k",
            models: "gpt-4o, gpt-4o-mini , gpt-3.5-turbo");

        Assert.Equal(new[] { "gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo" }, template.Models.ToArray());
    }

    // --- ResolveKeyForTemplate ---

    [Fact]
    public void ResolveKeyForTemplate_returns_found_key()
    {
        WriteTemplate("testprov.yaml", """
            id: testprov
            label: "Test"
            api: openai-completions
            baseUrl: https://x.example/v1
            apiKey: ${TESTPROV_UNIQ_KEY_8a3f}
            models:
              - id: m1
            """);
        File.WriteAllText(_animaEnvPath, "TESTPROV_UNIQ_KEY_8a3f=resolved\n");

        var templates = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);
        var selected = templates[0];

        var (resolved, key) = OnboardingFlow.ResolveKeyForTemplate(selected, _animaEnvPath);

        Assert.True(resolved);
        Assert.Equal("resolved", key);
    }

    [Fact]
    public void ResolveKeyForTemplate_returns_false_when_missing()
    {
        WriteTemplate("missing.yaml", """
            id: missing
            label: "M"
            api: openai-completions
            baseUrl: https://x.example/v1
            apiKey: ${MISSINGPROV_UNIQ_KEY_2d7c}
            models:
              - id: m1
            """);
        File.WriteAllText(_animaEnvPath, "");

        var templates = OnboardingFlow.ListImportableTemplates(_providersDir, _animaEnvPath);
        var (resolved, key) = OnboardingFlow.ResolveKeyForTemplate(templates[0], _animaEnvPath);

        Assert.False(resolved);
        Assert.Null(key);
    }
}
