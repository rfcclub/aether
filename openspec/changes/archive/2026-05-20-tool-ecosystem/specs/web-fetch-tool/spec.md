## ADDED Requirements

### Requirement: Fetch web page content

The system SHALL provide a `web_fetch` tool that fetches a URL and returns parsed text content.

#### Scenario: Fetch HTML page
- **WHEN** agent calls `web_fetch` with `{"url": "https://example.com"}`
- **THEN** system SHALL fetch the page, parse HTML, strip scripts/styles, and return plain text content (max 100KB)

#### Scenario: Fetch with timeout
- **WHEN** target server does not respond within 15 seconds
- **THEN** system SHALL return `ToolResult.Failure("web_fetch: request timed out after 15s")`

#### Scenario: Response too large
- **WHEN** response body exceeds 5MB
- **THEN** system SHALL stop reading and return `ToolResult.Failure("web_fetch: response exceeds 5MB limit")`

#### Scenario: Non-HTTP URL
- **WHEN** agent calls `web_fetch` with `{"url": "file:///etc/passwd"}`
- **THEN** system SHALL return `ToolResult.Failure("web_fetch: only http and https URLs are allowed")`

#### Scenario: Private IP denied
- **WHEN** URL resolves to private/localhost IP range
- **THEN** system SHALL return `ToolResult.Failure("web_fetch: cannot fetch private network addresses")`

### Requirement: HTML to text conversion

The system SHALL convert HTML content to plain text by removing `<script>`, `<style>`, `<nav>`, `<footer>` elements and preserving paragraph structure.

#### Scenario: Basic HTML page
- **WHEN** page contains `<p>Hello</p><script>evil()</script>`
- **THEN** output SHALL contain "Hello" and SHALL NOT contain "evil()"
