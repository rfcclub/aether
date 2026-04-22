namespace Aether.Channels;

public readonly record struct InboundMessage(
    string Id,
    string ChannelName,
    string ChatId,
    string SenderId,
    string Text,
    DateTimeOffset Timestamp,
    bool IsFromBot = false)
{
    public string RouteKey => $"{ChannelName}:{ChatId}";
}
