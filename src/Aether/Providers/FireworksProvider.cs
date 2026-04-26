namespace Aether.Providers;

/// <summary>
/// Fireworks AI provider - OpenAI-compatible endpoint.
/// Primary unlimited tier for cost optimization.
/// Inherits common HTTP patterns from OpenAiCompatibleProviderBase.
/// </summary>
public sealed class FireworksProvider : OpenAiCompatibleProviderBase
{
    private readonly FireworksOptions _options;

    public override string Name => "fireworks";
    public override string Model => _options.Model;

    public FireworksProvider(HttpClient client, FireworksOptions options) : base(client)
    {
        _options = options;
    }

    protected override string GetEndpoint() => "chat/completions";
    protected override string GetApiKey() => _options.ApiKey;
    protected override string GetBaseUrl() => _options.BaseUrl;
}

public sealed record FireworksOptions(string ApiKey, string Model, string BaseUrl);