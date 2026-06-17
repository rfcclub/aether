## Why

Integrating native support for the Model Context Protocol (MCP) allows Aether to dynamically access external tool registries (like Filesystem search, GitHub integrations) and expose its own memory and local tools to other clients (like Cursor or the Claude CLI) over a standard protocol.

## What Changes

- Implement an `McpClient` to load and execute tools from stdio and SSE MCP servers.
- Implement an `McpServer` to expose Aether's internal memory and tools to external clients.
- Add an `mcp` configuration block to `.aether.json`.

## Capabilities

### New Capabilities
- `mcp-client-stdio`: Connects to local stdio-based MCP servers and registers their tools dynamically.
- `mcp-server-stdio`: Exposes Aether tools and memory over a stdio JSON-RPC interface.

### Modified Capabilities
- `tool-registry`: Extend registry to support dynamically registered MCP tools at runtime.

## Impact

- `src/Aether/Tooling/ToolRegistry.cs` (extended)
- `src/Aether/Agent/AetherSoul.cs` (routing tool calls to external MCP endpoints)
- Configuration format in `CONFIGURATION.md`

