using System.Text.Json;
using Aether.Channels;
using Aether.Providers;
using Aether.Tooling;

namespace Aether.Plugins;

public record HookContext
{
    public string AgentName { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string SessionId { get; init; } = "";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object?> Bag { get; init; } = new();
}

// ── Message pipeline contexts ──

public record OnMessageReceivedContext : HookContext
{
    public string ChatId { get; init; } = "";
    public string SenderId { get; init; } = "";
    public string ChannelName { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Dropped { get; set; }
    public string? OverrideText { get; set; }
}

public record OnMessageRoutedContext : HookContext
{
    public InboundMessage Message { get; init; }
    public string ResolvedAgentName { get; init; } = "";
    public bool RerouteToAgent { get; set; }
    public string? RerouteAgentName { get; set; }
}

public record OnMessageSentContext : HookContext
{
    public string ChatId { get; init; } = "";
    public string Text { get; init; } = "";
    public string? OverrideText { get; set; }
    public bool Suppress { get; set; }
}

// ── LLM pipeline contexts ──

public record PreLlmCallContext : HookContext
{
    public string SystemPrompt { get; set; } = "";
    public IReadOnlyList<LlmMessage> Messages { get; init; } = Array.Empty<LlmMessage>();
    public string ModelName { get; init; } = "";
    public string ProviderName { get; init; } = "";
    public bool ShouldEscalate { get; set; }
    public int EstimatedTokens { get; init; }
}

public record PostLlmCallContext : HookContext
{
    public LlmResponse Response { get; init; } = null!;
    public int TokensUsed { get; init; }
    public TimeSpan Latency { get; init; }
    public bool ShouldRetry { get; set; }
    public string? RetryReason { get; set; }
    public string? OverrideContent { get; set; }
}

// ── Tool pipeline contexts ──

public record PreToolUseContext : HookContext
{
    public string ToolName { get; init; } = "";
    public JsonElement Arguments { get; init; }
    public string RawArguments { get; init; } = "";
    public ToolRisk Risk { get; init; }
    public bool Denied { get; set; }
    public string? DenyReason { get; set; }
    public JsonElement? OverrideArguments { get; set; }
}

public record PostToolUseContext : HookContext
{
    public string ToolName { get; init; } = "";
    public JsonElement Arguments { get; init; }
    public object? Result { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public object? OverrideResult { get; set; }
}

// ── Session lifecycle contexts ──

public record OnSessionStartContext : HookContext
{
    public bool IsNewSession { get; init; }
}

public record OnSessionCompactContext : HookContext
{
    public int TokensBefore { get; init; }
    public int TokensAfter { get; set; }
    public string? Summary { get; init; }
}

// ── Memory operation contexts ──

public record OnMemoryWriteContext : HookContext
{
    public string MemoryLayer { get; init; } = "";
    public string Content { get; init; } = "";
    public float Confidence { get; init; }
    public bool Denied { get; set; }
    public string? DenyReason { get; set; }
}

// ── Agent lifecycle contexts ──

public record OnAgentStartContext : HookContext
{
    public bool IsFirstBoot { get; init; }
    public string AgentVersion { get; init; } = "";
    public IReadOnlyList<string> BootFiles { get; init; } = Array.Empty<string>();
}

public record OnHeartbeatTickContext : HookContext
{
    public int TickNumber { get; init; }
    public TimeSpan TimeSinceLastTick { get; init; }
    public string? HeartbeatContent { get; init; }
}
