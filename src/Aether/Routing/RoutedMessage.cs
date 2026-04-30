using Aether.Channels;

namespace Aether.Routing;

public readonly record struct RoutedMessage(
    InboundMessage Inbound,
    string AgentName,
    string WorkspacePath,
    string Prompt);
