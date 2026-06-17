# Intent: mcp-support

## Raw Request

"còn gì em có thể làm để có thể hoàn thiện Aether ? Như MCP support, tool support, plugin, A2A protocol, hay những gì tà đạo em nghĩ ra ?" -> "dùng LoomKit tạo intent và specs rồi bỏ trong thư mục openspec, và làm B, C, A, D" (B: MCP support)

## Problem

Aether currently relies on predefined built-in C# tools or manual JSON registrations. Extending its capability to external search, git wrappers, or file indexers requires writing custom C# class executors. To tap into the wide ecosystem of Model Context Protocol (MCP) servers and expose Aether to IDE extensions/Claude CLI, Aether needs native MCP Client and Server implementations.

## Desired Outcome

Aether can:
1. Act as an MCP Client: Connect to stdio or SSE-based MCP servers, discover their tools, translate them into Aether `LlmTool` specifications, and execute them.
2. Act as an MCP Server: Expose Aether's tool execution engine and memory queries over stdio/SSE so external tools or other agent runners (e.g. Claude CLI) can call Aether's tools.

## Users / Actors

- **Developer / Agent:** Invokes tools hosted on external MCP servers.
- **External Client (e.g. Claude Code / Cursor):** Connects to Aether as an MCP server to query memories or run tools.

## Current Context

- C# backend (.NET 10).
- `ToolRegistry.cs` handles tool definitions.
- `ToolExecutor.cs` runs shell/file operations.
- `AetherSoul.cs` executes the tool-calling loop.

## Proposed Direction

- Build an `McpClientService` that spawns stdio sub-processes (node/python MCP servers) or queries SSE endpoints, retrieves their JSON schemas, and registers them dynamically to the `ToolRegistry`.
- Build an `McpServerEndpoint` using ASP.NET Core or Stdio pipes implementing the MCP protocol specification, exposing `read_memory`, `search_memory`, and registered tools.

## Scope

- Stdio-based MCP Client hosting.
- Dynamic tool translation (`McpTool` <=> `LlmTool`).
- JSON-RPC over stdio pipeline.
- Stdio-based MCP Server exposing Aether memory and tool execution.

## Non-Goals

- A graphical UI manager for configuring MCP servers (handled via appsettings.json).
- Custom encryption layer over stdio.

## Constraints

- Stdio sub-processes must be cleaned up properly on Aether host exit.
- Non-blocking asynchronous JSON-RPC communication.

## Success Criteria

- Aether can successfully list, validate, and execute tools from a Node.js-based filesystem or fetch MCP server.
- External MCP clients can list Aether's memory querying tools.

## Risks

- Subprocess leaks if Aether crashes.
  - Mitigation: Process tree job objects / clean disposal on token cancellation.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- Should HTTP/SSE client-mode be supported? -> Stdio client-mode is high priority; HTTP/SSE can be secondary.

## Assumptions

- Stdio-based MCP servers are pre-installed in the environment (e.g., node, python available on macOS PATH).

## Spec Seeds

- Aether configuration supports an `mcp:clients` dictionary defining servers to launch.
- Dynamic tool invocation intercepts target commands and routes them through standard input/output JSON-RPC.

## Intent Approval

Status: APPROVED

Approved by: Thoor
Date: 2026-06-16

