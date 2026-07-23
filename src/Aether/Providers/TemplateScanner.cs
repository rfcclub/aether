namespace Aether.Providers;

/// <summary>
/// One parsed providers.d template (from ~/.anima/providers.d/*.yaml).
/// </summary>
public sealed record ProviderTemplate
{
    /// <summary>Template id, e.g. "nahcrof", "gemini".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human label, e.g. "Nahcrof AI". Quotes already stripped.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Raw api field, e.g. "openai-completions", "anthropic-messages", "generic".</summary>
    public string Api { get; init; } = string.Empty;

    /// <summary>Mapped aether SpecProviderEntry.Type ("openai"/"anthropic"), or null if unsupported.</summary>
    public string? MappedType { get; init; }

    /// <summary>Base URL, e.g. "https://crof.ai/v1".</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Raw apiKey reference, e.g. "${NAHCROF_API_KEY}", "${OAUTH:openai}", or literal key.</summary>
    public string ApiKeyRef { get; init; } = string.Empty;

    /// <summary>Model ids from the models list. Empty if no models.</summary>
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();

    /// <summary>True when MappedType is not null (i.e. api is openai-completions or anthropic-messages).</summary>
    public bool Supported => MappedType is not null;
}

/// <summary>
/// Scans ~/.anima/providers.d for provider YAML templates and parses them into
/// <see cref="ProviderTemplate"/> records. Hand-rolled parser for the fixed YAML subset
/// used by anima providers.d (6 top-level keys; models is a list of "- id: X" entries).
/// No external YAML dependency.
/// </summary>
public static class TemplateScanner
{
    private const string ModelsKey = "models";
    private static readonly HashSet<string> RequiredKeys = new(StringComparer.Ordinal) { "id", "api", "baseUrl" };

    /// <summary>
    /// Scan <paramref name="providersDir"/> (default ~/.anima/providers.d) for *.yaml files
    /// and parse each. Missing/empty directory returns an empty list. Malformed files are skipped.
    /// </summary>
    public static IReadOnlyList<ProviderTemplate> ScanTemplates(string? providersDir = null)
    {
        var dir = providersDir ?? DefaultProvidersDir();
        if (!Directory.Exists(dir))
            return Array.Empty<ProviderTemplate>();

        var result = new List<ProviderTemplate>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var template = ParseTemplateFile(file);
            if (template is not null)
                result.Add(template);
        }
        return result;
    }

    /// <summary>
    /// Parse a single provider YAML file. Returns null on malformed input or missing required fields.
    /// </summary>
    public static ProviderTemplate? ParseTemplateFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }

        return ParseTemplateText(text);
    }

    /// <summary>
    /// Map providers.d <c>api</c> field to aether <see cref="SpecProviderEntry"/> Type.
    /// Returns null for unsupported adapters (generic, unknown).
    /// </summary>
    public static string? MapApiToType(string api)
    {
        if (string.IsNullOrWhiteSpace(api))
            return null;

        return api.ToLowerInvariant() switch
        {
            "openai-completions" => "openai",
            "anthropic-messages" => "anthropic",
            _ => null
        };
    }

    internal static ProviderTemplate? ParseTemplateText(string text)
    {
        // Parse line-by-line. Subset: top-level "key: value" lines, plus a "models:" section
        // whose subsequent indented "- id: X" lines are collected as model ids.
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var models = new List<string>();
        var inModels = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            // blank or comment line
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            // model entry: "  - id: X"
            if (line.Length > 0 && (line.StartsWith("  -", StringComparison.Ordinal) || line.StartsWith("\t-")))
            {
                if (inModels)
                {
                    var modelId = ParseModelEntry(trimmed);
                    if (modelId is not null)
                        models.Add(modelId);
                }
                else
                {
                    // a list item appeared outside a models section — not our subset
                    return null;
                }
                continue;
            }

            // a top-level key: value
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                inModels = false; // leaving any prior models section

                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                    return null; // not "key: value" — malformed for our subset

                var key = trimmed.Substring(0, colon).Trim();
                var value = trimmed.Substring(colon + 1).Trim();

                if (key.Equals(ModelsKey, StringComparison.Ordinal))
                {
                    // "models: []" → empty inline list; "models:" → block list follows
                    inModels = true;
                    if (value.Length > 0 && value != "[]")
                    {
                        // inline list not supported beyond [] — fail closed
                        return null;
                    }
                    fields[ModelsKey] = "[]";
                    continue;
                }

                // unexpected nested mapping (e.g. "foo:\n  bar: baz") — not our subset
                if (value.Length == 0)
                    return null;

                fields[key] = Unquote(value);
            }
            else
            {
                // indented non-list line inside models section that isn't "- id: X" → malformed
                if (inModels)
                    return null;
            }
        }

        // validate required fields
        foreach (var required in RequiredKeys)
        {
            if (!fields.TryGetValue(required, out var v) || string.IsNullOrWhiteSpace(v))
                return null;
        }

        var api = fields["api"];
        var mappedType = MapApiToType(api);

        return new ProviderTemplate
        {
            Id = fields["id"],
            Label = fields.TryGetValue("label", out var label) ? label : fields["id"],
            Api = api,
            MappedType = mappedType,
            BaseUrl = fields["baseUrl"],
            ApiKeyRef = fields.TryGetValue("apiKey", out var apiKeyValue) ? apiKeyValue : string.Empty,
            Models = models
        };
    }

    private static string? ParseModelEntry(string listItem)
    {
        // expected: "- id: X" (after trim)
        var s = listItem.TrimStart('-', ' ', '\t');
        var colon = s.IndexOf(':');
        if (colon <= 0)
            return null;
        var entryKey = s.Substring(0, colon).Trim();
        if (!entryKey.Equals("id", StringComparison.Ordinal))
            return null;
        var value = s.Substring(colon + 1).Trim();
        return Unquote(value);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') ||
                (value[0] == '\'' && value[^1] == '\''))
            {
                return value.Substring(1, value.Length - 2);
            }
        }
        return value;
    }

    private static string DefaultProvidersDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".anima", "providers.d");
    }
}
