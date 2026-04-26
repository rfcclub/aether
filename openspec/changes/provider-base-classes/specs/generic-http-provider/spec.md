## ADDED Requirements

### Requirement: Generic HTTP provider for unknown endpoints
`GenericHttpProvider` SHALL implement `ILLMProvider` with configurable endpoint, auth, and payload mapping.

#### Scenario: Custom/unknown provider
- **WHEN** user needs to connect to provider not explicitly supported
- **THEN** they configure GenericHttpProvider with base URL, endpoint, auth

### Requirement: Configurable auth
Generic provider SHALL support API key auth (header or body), Bearer token, or custom header.

#### Scenario: Custom auth header
- **WHEN** provider requires non-standard auth
- **THEN** GenericHttpProvider accepts custom auth header name/value

### Requirement: JSON payload mapping
Generic provider SHALL serialize request via JSON and parse response assuming `{"content": "..."}` or `{"choices": [{"message": {"content": "..."}}]}` structure.

#### Scenario: Response parsing
- **WHEN** provider returns JSON response
- **THEN** GenericHttpProvider extracts content from standard locations