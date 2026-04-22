namespace Aether.Sessions;

public sealed record Session(string Id, string GroupFolder, DateTimeOffset CreatedAt, DateTimeOffset LastActivity);

public sealed record SessionMessage(string Role, string Content, DateTimeOffset Timestamp);
