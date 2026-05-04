namespace Aether.Agent;

public sealed record ToolCall(string Name, IReadOnlyDictionary<string, string> Arguments);

public sealed record ToolResult(bool Succeeded, string Output, string? Error = null);
