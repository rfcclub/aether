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
                return CreateAnthropic(entry, providerName, logger);
            case "openrouter":
                return CreateOpenRouter(entry, providerName);
            case "openai":
                return CreateOpenAiCompatible(entry, providerName);
            default:
                logger.LogWarning("Unknown provider type '{Type}' for provider '{Name}', falling back to OpenAI-compatible", entry.Type, providerName);
                return CreateOpenAiCompatible(entry, providerName);
        }
    }

    private static ILLMProvider CreateOpenAiCompatible(SpecProviderEntry entry, string providerName)
    {
        var baseUrl = entry.BaseUrl ?? "http://localhost:11434/v1";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var options = new GenericHttpOptions(
            Name: providerName,
            Model: entry.Model,
            ApiKey: entry.ApiKey ?? "",
            BaseUrl: baseUrl,
            Endpoint: "chat/completions",
            AuthHeader: "Bearer");
        return new GenericHttpProvider(client, options);
    }

    private static ILLMProvider CreateOpenRouter(SpecProviderEntry entry, string providerName)
    {
        var baseUrl = entry.BaseUrl ?? "https://openrouter.ai/api/v1";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var options = new OpenRouterOptions(
            ApiKey: entry.ApiKey ?? "",
            Model: entry.Model,
            BaseUrl: baseUrl);
        return new OpenRouterProvider(client, options);
    }

    private static ILLMProvider CreateAnthropic(SpecProviderEntry entry, string providerName, ILogger logger)
    {
        var baseUrl = entry.BaseUrl ?? "https://api.anthropic.com";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        var options = new AnthropicOptions(
            ApiKey: entry.ApiKey ?? "",
            Model: entry.Model,
            BaseUrl: baseUrl,
            Name: providerName);
        return new AnthropicProvider(client, options);
    }
}
