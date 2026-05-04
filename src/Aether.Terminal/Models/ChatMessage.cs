namespace Aether.Terminal.Models;

public enum ChatRole { User, Assistant, Tool, System }

public record ChatMessage(
    string Id,
    ChatRole Role,
    string Content,
    string? ToolName = null,
    string? ToolResult = null,
    DateTime Timestamp = default
);
