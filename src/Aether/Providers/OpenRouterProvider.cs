using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aether.Providers;

public sealed class OpenRouterProvider : OpenAiCompatibleProviderBase
{
    private readonly OpenRouterOptions _options;

    public override string Name => "openrouter";
    public override string Model => _options.Model;

    public OpenRouterProvider(HttpClient client, OpenRouterOptions options) : base(client)
    {
        _options = options;
    }

    protected override string GetEndpoint() => "chat/completions";
    protected override string GetApiKey() => _options.ApiKey;
    protected override string GetBaseUrl() => _options.BaseUrl;
}

public sealed record OpenRouterOptions(string ApiKey, string Model, string BaseUrl);
