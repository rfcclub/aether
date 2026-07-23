using System.Text.Json;

namespace Aether.Mcp;

/// <summary>
/// Options for MCP (Model Context Protocol) integration.
/// Controls both client (connecting to external MCP servers) and server (exposing Aether tools via MCP) modes.
/// </summary>
public sealed record McpOptions
{
    /// <summary>
    /// Whether MCP integration is enabled.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// External MCP servers to connect to as a client.
    /// </summary>
    public List<McpClientConfig> Clients { get; init; } = new();

    /// <summary>
    /// Local MCP server configuration (exposes Aether tools to external clients).
    /// </summary>
    public McpServerConfig? Server { get; init; }
}

/// <summary>
/// Configuration for an external MCP server connection.
/// </summary>
public sealed record McpClientConfig
{
    /// <summary>
    /// Human-readable name for this connection.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Command to spawn (e.g., "node", "python", "dotnet").
    /// </summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// Arguments passed to the command.
    /// </summary>
    public List<string> Args { get; init; } = new();

    /// <summary>
    /// Environment variables (optional).
    /// </summary>
    public Dictionary<string, string>? Env { get; init; }
}

/// <summary>
/// Configuration for the local MCP server that exposes Aether's tools.
/// </summary>
public sealed record McpServerConfig
{
    /// <summary>
    /// Whether the MCP server is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
