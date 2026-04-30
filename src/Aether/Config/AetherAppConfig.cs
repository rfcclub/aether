namespace Aether.Config;

public sealed record AetherAppConfig
{
    public ProviderSection Providers { get; init; } = new();
    public ChannelSection ChannelDefaults { get; init; } = new();
    public SandboxSection Sandbox { get; init; } = new();
    public Dictionary<string, AgentEntryConfig> Agents { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public MetaSection Meta { get; init; } = new();
    public WizardSection Wizard { get; init; } = new();
}

public sealed record ProviderSection
{
    public OpenRouterProviderConfig OpenRouter { get; init; } = new();
    public AnthropicProviderConfig Anthropic { get; init; } = new();
    public FireworksProviderConfig Fireworks { get; init; } = new();
}

public sealed record OpenRouterProviderConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public int TimeoutSeconds { get; init; } = 90;
}

public sealed record AnthropicProviderConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "claude-3-5-sonnet-20241022";
    public string BaseUrl { get; init; } = "https://api.anthropic.com";
}

public sealed record FireworksProviderConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "accounts/fireworks/models/deepseek-v3-0324";
    public string BaseUrl { get; init; } = "https://api.fireworks.ai/inference/v1";
}

public sealed record ChannelSection
{
    public TelegramChannelConfig Telegram { get; init; } = new();
    public WebSocketChannelConfig WebSocket { get; init; } = new();
}

public sealed record TelegramChannelConfig
{
    public bool Enabled { get; init; }
    public string BotToken { get; init; } = string.Empty;
}

public sealed record WebSocketChannelConfig
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 5099;
}

public sealed record SandboxSection
{
    public string Type { get; init; } = "bwrap";
    public int TimeoutMs { get; init; } = 30000;
    public int MaxMemoryMb { get; init; } = 512;
}

public sealed record MetaSection
{
    public string? LastTouchedVersion { get; init; }
}

public sealed record WizardSection
{
    public string? LastRunAt { get; init; }
    public string? LastRunVersion { get; init; }
    public string? LastRunCommand { get; init; }
}
