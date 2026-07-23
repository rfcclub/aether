using Aether.Providers;

namespace Aether.Cli;

/// <summary>Onboarding mode chosen by the user (or defaulted).</summary>
public enum OnboardingMode
{
    /// <summary>Import a provider from ~/.anima/providers.d templates.</summary>
    Import,
    /// <summary>Manually configure a custom provider.</summary>
    Raw,
    /// <summary>OAuth-based login (placeholder — manual key fallback).</summary>
    OAuth
}

/// <summary>Protocol options for Raw mode.</summary>
public enum RawProtocol
{
    OpenAiChat,
    OpenAiResponses,
    AnthropicMessages
}

/// <summary>Key availability status for a template, shown in the import list.</summary>
public enum KeyStatus
{
    Found,
    Missing,
    OAuth,
    Unsupported
}

/// <summary>
/// A template row shown in the import list, with resolved key status.
/// </summary>
public sealed record ImportTemplateRow(
    string Id,
    string Label,
    string Api,
    string BaseUrl,
    KeyStatus KeyStatus,
    bool Supported,
    ProviderTemplate Template);

/// <summary>
/// Testable, pure logic for the 3-mode onboarding wizard. The Spectre.Console prompts
/// in <c>FirstRunWizard</c> delegate to these methods so the mode/template/key logic is
/// verifiable without an <c>IAnsiConsole</c> fixture.
/// </summary>
public static class OnboardingFlow
{
    /// <summary>Mode labels shown by the Spectre SelectionPrompt.</summary>
    public const string ImportLabel = "Import from providers.d";
    public const string RawLabel = "Custom Setup (Raw)";
    public const string OAuthLabel = "OAuth Login";

    private static readonly Dictionary<string, OnboardingMode> ModeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        [ImportLabel] = OnboardingMode.Import,
        [RawLabel] = OnboardingMode.Raw,
        [OAuthLabel] = OnboardingMode.OAuth
    };

    /// <summary>Map a chosen Spectre prompt label to an <see cref="OnboardingMode"/>. Defaults to Import.</summary>
    public static OnboardingMode ResolveModeFromChoice(string? choice)
    {
        if (string.IsNullOrEmpty(choice))
            return OnboardingMode.Import;
        return ModeLabels.TryGetValue(choice, out var mode) ? mode : OnboardingMode.Import;
    }

    /// <summary>Scan providers.d and annotate each template with its resolved key status.</summary>
    public static IReadOnlyList<ImportTemplateRow> ListImportableTemplates(
        string? providersDir,
        string? animaEnvPath)
    {
        var templates = TemplateScanner.ScanTemplates(providersDir);
        var envOptions = new EnvResolveOptions { AnimaEnvPath = animaEnvPath };

        var rows = new List<ImportTemplateRow>(templates.Count);
        foreach (var t in templates)
        {
            KeyStatus status;
            if (!t.Supported)
            {
                status = KeyStatus.Unsupported;
            }
            else
            {
                var resolved = EnvResolver.ResolveApiKeyRef(t.ApiKeyRef, envOptions);
                status = resolved.IsOAuth ? KeyStatus.OAuth
                    : resolved.Resolved ? KeyStatus.Found
                    : KeyStatus.Missing;
            }
            rows.Add(new ImportTemplateRow(
                t.Id, t.Label, t.Api, t.BaseUrl, status, t.Supported, t));
        }
        return rows;
    }

    /// <summary>Find a template row by id (case-insensitive). Null if not found.</summary>
    public static ImportTemplateRow? ResolveTemplateSelection(
        IReadOnlyList<ImportTemplateRow> rows, string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return null;
        return rows.FirstOrDefault(r =>
            string.Equals(r.Id, templateId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Build a <see cref="ProviderTemplate"/> from raw-mode inputs.</summary>
    public static ProviderTemplate BuildRawTemplate(
        string name,
        string url,
        RawProtocol protocol,
        string apiKey,
        object models)
    {
        var api = protocol switch
        {
            RawProtocol.AnthropicMessages => "anthropic-messages",
            _ => "openai-completions"
        };
        var mappedType = protocol switch
        {
            RawProtocol.AnthropicMessages => "anthropic",
            _ => "openai"
        };

        var modelList = models switch
        {
            string s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .ToList(),
            IEnumerable<string> enumerable => enumerable.ToList(),
            _ => new List<string>()
        };

        return new ProviderTemplate
        {
            Id = name,
            Label = name,
            Api = api,
            MappedType = mappedType,
            BaseUrl = url,
            ApiKeyRef = apiKey,
            Models = modelList
        };
    }

    /// <summary>
    /// Resolve the API key for a selected template. Returns (true, key) if found,
    /// (false, null) if missing. OAuth is treated as not-resolved here (caller handles
    /// the OAuth prompt separately).
    /// </summary>
    public static (bool Resolved, string? Key) ResolveKeyForTemplate(
        ImportTemplateRow row,
        string? animaEnvPath,
        string? explicitKey = null)
    {
        if (!string.IsNullOrEmpty(explicitKey))
            return (true, explicitKey);

        var envOptions = new EnvResolveOptions { AnimaEnvPath = animaEnvPath };
        var result = EnvResolver.ResolveApiKeyRef(row.Template.ApiKeyRef, envOptions);
        if (result.Resolved && !result.IsOAuth)
            return (true, result.Value);
        return (false, null);
    }

    /// <summary>The protocol label displayed by the Spectre prompt.</summary>
    public static string ProtocolLabel(RawProtocol protocol) => protocol switch
    {
        RawProtocol.OpenAiChat => "OpenAI Chat Completions",
        RawProtocol.OpenAiResponses => "OpenAI Responses API",
        RawProtocol.AnthropicMessages => "Anthropic Messages API",
        _ => "OpenAI Chat Completions"
    };

    /// <summary>Parse a protocol label back to a <see cref="RawProtocol"/>.</summary>
    public static RawProtocol ParseProtocolLabel(string? label) => label switch
    {
        "OpenAI Responses API" => RawProtocol.OpenAiResponses,
        "Anthropic Messages API" => RawProtocol.AnthropicMessages,
        _ => RawProtocol.OpenAiChat
    };
}
