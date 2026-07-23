using System.Text.Json;
using Aether.Config;

namespace Aether.Providers;

/// <summary>
/// Converts a parsed <see cref="ProviderTemplate"/> + resolved API key into a
/// <see cref="SpecProviderEntry"/> and writes it into the <c>providers</c> map of
/// <c>~/.aether/config.json</c>. Shared by <c>aether provider add</c> CLI and
/// <c>FirstRunWizard</c> so config-writing logic lives in one place.
/// </summary>
public static class ProviderRegistrar
{
    // Write config.json with snake_case keys preserved (api_key, base_url) — matches what
    // ConfigLoader reads via GetValue<string>("api_key") and existing config.json files.
    // Top-level keys (providers/agents/meta) have no underscores so case policy is a no-op.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Convert a parsed template + resolved key into a <see cref="SpecProviderEntry"/>.
    /// Throws <see cref="InvalidOperationException"/> for unsupported adapters (generic/unknown).
    /// </summary>
    public static SpecProviderEntry ToSpecProviderEntry(ProviderTemplate template, string apiKey)
    {
        if (template.MappedType is null)
            throw new InvalidOperationException(
                $"Provider '{template.Id}' uses unsupported API format '{template.Api}'. " +
                "Use Raw mode with an OpenAI-compatible proxy endpoint instead.");

        var models = template.Models.Count > 0
            ? template.Models.ToList()
            : new List<string>();

        return new SpecProviderEntry
        {
            Type = template.MappedType,
            BaseUrl = template.BaseUrl,
            ApiKey = apiKey,
            Model = template.Models.Count > 0 ? template.Models[0] : string.Empty,
            Models = models
        };
    }

    /// <summary>
    /// Write a provider entry into the <c>providers</c> map of config.json at
    /// <paramref name="aetherDir"/>. Creates the file if missing, overwrites an existing
    /// entry with the same name, and preserves all other providers/agents/meta.
    /// </summary>
    public static async Task WriteProviderAsync(
        string aetherDir,
        string providerName,
        ProviderTemplate template,
        string apiKey,
        CancellationToken ct = default)
    {
        var entry = ToSpecProviderEntry(template, apiKey);

        var configPath = Path.Combine(aetherDir, "config.json");
        Dictionary<string, object?> config;

        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath, ct);
            config = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions) ?? new();
        }
        else
        {
            config = new Dictionary<string, object?>();
        }

        var providers = config.TryGetValue("providers", out var p) && p is not null
            ? DeserializeToDict((JsonElement)p)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        providers[providerName] = new Dictionary<string, object?>
        {
            ["type"] = entry.Type,
            ["base_url"] = entry.BaseUrl,
            ["api_key"] = entry.ApiKey,
            ["model"] = entry.Model,
            ["models"] = entry.Models
        };

        config["providers"] = providers;

        var updated = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, updated, ct);
    }

    private static Dictionary<string, object?> DeserializeToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ElementToObject(prop.Value);
        }
        return dict;
    }

    private static object? ElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => (object?)element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : (object)element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => DeserializeToDict(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ElementToObject).ToList(),
        _ => element.GetRawText()
    };
}
