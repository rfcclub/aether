## MODIFIED Requirements

### Requirement: Built-in tools registered at startup

The following tools SHALL be registered at startup with real implementations: `read`, `write`, `edit`, `bash`, `glob`, `grep`, `web_search`, `web_fetch`.

#### Scenario: Built-in tool available immediately
- **WHEN** the host starts
- **THEN** all eight built-in tools SHALL be resolvable from `IToolRegistry` without any configuration

#### Scenario: Each built-in tool has real implementation
- **WHEN** any built-in tool is executed with valid arguments
- **THEN** the tool SHALL perform actual filesystem, shell, or web operations (not return a passive stub response)

## ADDED Requirements

### Requirement: Web tools require provider configuration

The `web_search` and `web_fetch` tools SHALL be registered at startup but SHALL return a clear error if their required providers are not configured.

#### Scenario: web_search without Tally API key
- **WHEN** agent calls `web_search` but no Tally API key is configured
- **THEN** tool SHALL return `ToolResult.Failure("web_search: Tally API key not configured. Set TALLY_API_KEY env var or add tally provider config.")`
