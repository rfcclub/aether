## ADDED Requirements

### Requirement: Web search via Tally API

The system SHALL provide a `web_search` tool that queries Tally Search API and returns formatted search results.

#### Scenario: Basic search query
- **WHEN** agent calls `web_search` with `{"query": "latest AI news"}`
- **THEN** system SHALL call Tally Search API with the query and return up to 10 results with title, URL, and snippet

#### Scenario: Search with limit
- **WHEN** agent calls `web_search` with `{"query": "C# dotnet 10", "limit": 5}`
- **THEN** system SHALL return at most 5 results

#### Scenario: Tally API key not configured
- **WHEN** no Tally API key is found in config or environment variable `TALLY_API_KEY`
- **THEN** system SHALL return `ToolResult.Failure("web_search: Tally API key not configured")`

#### Scenario: API rate limit exceeded
- **WHEN** Tally API returns HTTP 429
- **THEN** system SHALL return `ToolResult.Failure("web_search: rate limited, retry after N seconds")`

#### Scenario: Network error
- **WHEN** Tally API is unreachable (timeout, DNS failure)
- **THEN** system SHALL return `ToolResult.Failure("web_search: request failed - <error>")`

### Requirement: Tally API key configuration

The system SHALL resolve Tally API key in order: environment variable `TALLY_API_KEY` → config `providers.tally.api_key` → agent provider config.

#### Scenario: API key from environment
- **WHEN** `TALLY_API_KEY` env var is set
- **THEN** web_search tool SHALL use that key for API calls
