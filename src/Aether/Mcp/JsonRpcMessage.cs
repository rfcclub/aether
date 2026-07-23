using System.Text.Json;

namespace Aether.Mcp;

/// <summary>
/// Represents a JSON-RPC 2.0 message used in MCP communication.
/// </summary>
public sealed record JsonRpcMessage
{
    /// <summary>
    /// JSON-RPC version — always "2.0".
    /// </summary>
    public string Jsonrpc { get; init; } = "2.0";

    /// <summary>
    /// Request/response identifier. May be null for notifications.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Method name (present in requests).
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// Method parameters (present in requests).
    /// </summary>
    public JsonElement? Params { get; init; }

    /// <summary>
    /// Result payload (present in successful responses).
    /// </summary>
    public JsonElement? Result { get; init; }

    /// <summary>
    /// Error information (present in error responses).
    /// </summary>
    public JsonRpcError? Error { get; init; }
}

/// <summary>
/// JSON-RPC error object.
/// </summary>
public sealed record JsonRpcError
{
    public int Code { get; init; }
    public string Message { get; init; } = "";
    public JsonElement? Data { get; init; }
}

/// <summary>
/// MCP tool definition matching the Model Context Protocol schema.
/// </summary>
public sealed record McpToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public JsonElement? InputSchema { get; init; }
}

/// <summary>
/// Request to call an MCP tool.
/// </summary>
public sealed record CallToolRequest
{
    public string Name { get; init; } = "";
    public Dictionary<string, object>? Arguments { get; init; }
}

/// <summary>
/// Response from calling an MCP tool.
/// </summary>
public sealed record CallToolResponse
{
    public List<McpContentPart> Content { get; init; } = new();
    public bool IsError { get; init; }
}

/// <summary>
/// A content part in an MCP tool response.
/// </summary>
public sealed record McpContentPart
{
    public string Type { get; init; } = "text";
    public string Text { get; init; } = "";
}
