namespace Aether.Plugins;

[Flags]
public enum HookPoint
{
    None                = 0,

    // Message pipeline
    OnMessageReceived   = 1 << 0,
    OnMessageRouted     = 1 << 1,
    OnMessageSent       = 1 << 2,

    // LLM pipeline
    PreLlmCall          = 1 << 3,
    PostLlmCall         = 1 << 4,

    // Tool pipeline
    PreToolUse          = 1 << 5,
    PostToolUse         = 1 << 6,

    // Session lifecycle
    OnSessionStart      = 1 << 7,
    OnSessionCompact    = 1 << 8,
    OnSessionEnd        = 1 << 9,

    // Memory operations
    OnMemoryWrite       = 1 << 10,
    OnMemoryPromote     = 1 << 11,

    // Agent lifecycle
    OnAgentStart        = 1 << 12,
    OnAgentStop         = 1 << 13,
    OnHeartbeatTick     = 1 << 14,

    // Convenience combinations
    MessageLifecycle    = OnMessageReceived | OnMessageRouted | OnMessageSent,
    LlmLifecycle        = PreLlmCall | PostLlmCall,
    ToolLifecycle       = PreToolUse | PostToolUse,
    SessionLifecycle    = OnSessionStart | OnSessionCompact | OnSessionEnd,
    AgentLifecycle      = OnAgentStart | OnAgentStop | OnHeartbeatTick,
    All                 = ~0
}
