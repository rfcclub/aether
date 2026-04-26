namespace Aether.Providers;

/// <summary>
/// Anthropic provider - Claude via Anthropic API.
/// Safety tier for sensitive tasks.
/// Inherits Anthropic-specific patterns from AnthropicCompatibleProviderBase.
/// </summary>
public sealed class AnthropicProvider : AnthropicCompatibleProviderBase
{
    private readonly AnthropicOptions _options;

    public override string Name => "anthropic";
    public override string Model => _options.Model;

    public AnthropicProvider(HttpClient client, AnthropicOptions options) : base(client)
    {
        _options = options;
    }

    protected override string GetApiKey() => _options.ApiKey;
    protected override string GetBaseUrl() => _options.BaseUrl;
}

public sealed record AnthropicOptions(string ApiKey, string Model, string BaseUrl);