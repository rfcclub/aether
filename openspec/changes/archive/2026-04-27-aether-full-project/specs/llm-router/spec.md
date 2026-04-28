## ADDED Requirements

### Requirement: Unified CompleteAsync contract across all providers
All `ILLMProvider` implementations SHALL expose `CompleteAsync(LlmRequest, CancellationToken)` matching the contract used by `AetherSoul`. No adapter shims.

#### Scenario: ProviderRouter delegates via CompleteAsync
- **WHEN** `AetherSoul` calls `_llm.CompleteAsync(request, ct)`
- **THEN** `ProviderRouter.CompleteAsync` SHALL receive the call and route it to the appropriate provider group

### Requirement: Complexity scoring determines provider tier
The router SHALL compute a complexity score (0.0–1.0) for each request and escalate to the fallback tier (OpenRouter) only when the score exceeds `complexity_threshold` (default 0.8).

#### Scenario: Low-complexity stays on primary
- **WHEN** request complexity score ≤ 0.8
- **THEN** the router SHALL only try the Fireworks provider group

#### Scenario: High-complexity escalates to fallback
- **WHEN** request complexity score > 0.8
- **THEN** the router SHALL try the Fireworks group first; if it fails OR complexity demands it, try the OpenRouter group

### Requirement: Circuit breaker isolates failing providers
Each provider endpoint SHALL have an independent circuit breaker. After 3 consecutive failures, the circuit SHALL open and the endpoint SHALL be skipped for 60 seconds.

#### Scenario: Circuit opens after consecutive failures
- **WHEN** a provider endpoint fails 3 times in a row
- **THEN** subsequent requests SHALL skip that endpoint without attempting a call, logging "Circuit open for <endpoint>"

#### Scenario: Circuit closes after timeout
- **WHEN** 60 seconds elapse after a circuit opens
- **THEN** the next request SHALL attempt the endpoint (half-open probe); on success the circuit SHALL close

### Requirement: Streaming aggregated from active provider
`CompleteAsync` with `stream: true` SHALL return tokens as they arrive from whichever provider group is active.

#### Scenario: Streaming response delivered
- **WHEN** `LlmRequest.Stream = true` and the primary provider supports streaming
- **THEN** the router SHALL yield tokens via `IAsyncEnumerable<string>` as they arrive

#### Scenario: Provider does not support streaming
- **WHEN** the selected provider has `SupportsStreaming = false`
- **THEN** the router SHALL complete the request non-streaming and return the full response

### Requirement: Cost tracking per provider call
The router SHALL record token usage (input + output) and estimated cost per call to the `provider_usage` table in the database.

#### Scenario: Usage logged after each call
- **WHEN** any provider returns a successful response
- **THEN** the router SHALL insert a row into `provider_usage` with provider name, model, input tokens, output tokens, timestamp
