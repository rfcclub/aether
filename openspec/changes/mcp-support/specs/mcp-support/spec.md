## ADDED Requirements

### Requirement: mcp-client-registration
The MCP client system SHALL parse configured stdio server entries from the configuration and launch them as sub-processes.

#### Scenario: Registering Stdio MCP Tools
- **WHEN** Aether starts up with an MCP config block containing a filesystem node server
- **THEN** The filesystem MCP tools are registered in Aether's `ToolRegistry` and exposed to the LLM agent

### Requirement: mcp-client-execution
The tool executor SHALL route requests for MCP tools through standard input/output streams formatted as JSON-RPC 2.0 messages.

#### Scenario: Calling an MCP Tool
- **WHEN** The LLM calls an MCP tool named `fetch_url`
- **THEN** Aether sends a `tools/call` JSON-RPC request to the target subprocess and returns the `content` block to the LLM agent

