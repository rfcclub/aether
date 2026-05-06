using Aether.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Providers;

/// <summary>
/// Creates ILLMProvider instances from SpecProviderEntry config definitions.
/// Maps type strings to provider implementations:
///   "openai"     → GenericHttpProvider (OpenAI-compatible API)
///   "openrouter" → OpenRouterProvider (OpenAI-compatible with OpenRouter auth)
///   "anthropic"  → AnthropicProvider (Anthropic Messages API-compatible)
///   unknown      → GenericHttpProvider (with warning)
/// </summary>
public static class ProviderFactory
{
    public static ILLMProvider Create(SpecProviderEntry entry, string providerName, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var type = entry.Type?.ToLowerInvariant() ?? "openai";

        switch (type)
        {
            case "anthropic":
                return CreateAnthropic(entry, providerName, entry.Model, logger);
            case "openrouter":
                return CreateOpenRouter(entry, providerName, entry.Model);
            case "openai":
                return CreateOpenAiCompatible(entry, providerName, entry.Model);
            default:
                logger.LogWarning("Unknown provider type '{Type}' for provider '{Name}', falling back to OpenAI-compatible", entry.Type, providerName);
                return CreateOpenAiCompatible(entry, providerName, entry.Model);
        }
    }

    /// <summary>
    /// Creates one ILLMProvider per model in entry.Models, named "provider/modelId".
    /// Falls back to single-model Create() if Models list is empty.
    /// </summary>
    public static List<ILLMProvider> CreateAll(SpecProviderEntry entry, string providerName, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var models = entry.Models is { Count: > 0 } ? entry.Models : new List<string> { entry.Model };
        if (models.Count == 1 && models[0] == entry.Model)
            return new List<ILLMProvider> { Create(entry, providerName, logger) };

        var result = new List<ILLMProvider>(models.Count);
        var type = entry.Type?.ToLowerInvariant() ?? "openai";

        foreach (var model in models)
        {
            var modelProviderName = $"{providerName}/{model}";
            var provider = type switch
            {
                "anthropic" => CreateAnthropic(entry, modelProviderName, model, logger),
                "openrouter" => CreateOpenRouter(entry, modelProviderName, model),
                _ => CreateOpenAiCompatible(entry, modelProviderName, model),
            };
            result.Add(provider);
        }

        return result;
    }

    private static ILLMProvider CreateOpenAiCompatible(SpecProviderEntry entry, string providerName, string model)
    {
        var baseUrl = entry.BaseUrl ?? "http://localhost:11434/v1";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var options = new GenericHttpOptions(
            Name: providerName,
            Model: model,
            ApiKey: entry.ApiKey ?? "",
            BaseUrl: baseUrl,
            Endpoint: "chat/completions",
            AuthHeader: "Bearer");
        return new GenericHttpProvider(client, options);
    }

    private static ILLMProvider CreateOpenRouter(SpecProviderEntry entry, string providerName, string model)
    {
        var baseUrl = entry.BaseUrl ?? "https://openrouter.ai/api/v1";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var options = new OpenRouterOptions(
            ApiKey: entry.ApiKey ?? "",
            Model: model,
            BaseUrl: baseUrl);
        return new OpenRouterProvider(client, options);
    }

    private static ILLMProvider CreateAnthropic(SpecProviderEntry entry, string providerName, string model, ILogger logger)
    {
        var baseUrl = entry.BaseUrl ?? "https://api.anthropic.com";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var options = new AnthropicOptions(
            ApiKey: entry.ApiKey ?? "",
            Model: model,
            BaseUrl: baseUrl,
            Name: providerName);
        return new AnthropicProvider(client, options);
    }
}
