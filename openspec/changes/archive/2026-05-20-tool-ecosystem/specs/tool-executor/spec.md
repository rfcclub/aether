## ADDED Requirements

### Requirement: Web Search Tool

The executor SHALL support a `web_search` tool that queries Tally Search API via the `IWebSearchProvider` interface.

#### Scenario: web_search delegates to Tally provider
- **WHEN** `ExecuteAsync` is called with `Name = "web_search"` and arguments `{"query": "test"}`
- **THEN** the executor SHALL resolve `IWebSearchProvider` from DI and delegate the search

#### Scenario: web_search without provider
- **WHEN** no `IWebSearchProvider` is registered in DI
- **THEN** `web_search` SHALL return `ToolResult(false, "", "web_search: no search provider configured")`

### Requirement: Web Fetch Tool

The executor SHALL support a `web_fetch` tool that fetches URL content and returns parsed text.

#### Scenario: web_fetch returns page text
- **WHEN** `ExecuteAsync` is called with `Name = "web_fetch"` and arguments `{"url": "https://example.com"}`
- **THEN** the executor SHALL fetch the URL, strip HTML, and return plain text

### Requirement: Tool Implementation Bridge

The executor SHALL resolve `IToolImplementation` instances by name from DI for tools that specify an implementation, falling back to the hot-reloaded delegate if none is registered.

#### Scenario: Code implementation takes priority
- **WHEN** a tool is registered with `"implementation": "bash"` and an `IToolImplementation` with `Name = "bash"` exists in DI
- **THEN** `ExecuteAsync` SHALL call `IToolImplementation.ExecuteAsync` instead of the hot-reload delegate

#### Scenario: Fallback to hot-reload delegate
- **WHEN** a tool has no `"implementation"` field or the named implementation is not found
- **THEN** `ExecuteAsync` SHALL call the delegate stored in `ToolDefinition.Execute`

### Requirement: ISandboxContext available to implementations

The executor SHALL construct an `ISandboxContext` from agent spec `SpecToolsSection` and pass it to `IToolImplementation.ExecuteAsync`.

#### Scenario: Sandbox context reflects agent config
- **WHEN** agent spec has `tools.file.allowed_paths: ["/workspace"]`
- **THEN** `ISandboxContext.IsPathAllowed("/workspace/foo.txt")` SHALL return true and `IsPathAllowed("/etc/passwd")` SHALL return false
