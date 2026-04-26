## Context

Aether's `ProviderRouter` currently has OpenRouter provider. Need to add Fireworks AI (unlimited), Anthropic-compatible for Claude, and health monitoring for production resilience.

Provider tier design from OpenSpec:
- Primary: Fireworks (unlimited, cost-optimized)
- Fallback: OpenRouter (high quality, pay-per-use)
- Safety: Anthropic (for safety-sensitive tasks)

## Goals / Non-Goals

**Goals:**
- Add FireworksProvider (OpenAI-compatible) as primary unlimited provider
- Add AnthropicProvider for Claude models via OpenRouter
- Implement ProviderHealthMonitor with periodic health checks
- Integrate circuit breaker with health status

**Non-Goals:**
- Custom provider implementations (use existing OpenAI/Anthropic SDK patterns)
- Token counting accuracy (estimate based on char count)
- Multi-region failover within same provider

## Decisions

### 1. OpenAI-compatible for Fireworks

Fireworks AI exposes OpenAI-compatible API. Use OpenAI.NET SDK with base URL override.

```csharp
// Fireworks uses OpenAI-compatible endpoint
var client = new OpenAI.OpenAIClient(apiKey, new OpenAI.OpenAIClientOptions("https://api.fireworks.ai/inference/v1"));
```

### 2. Provider Health Monitoring

Health monitor runs as background service, checks each provider every 30s.
Results fed into circuit breaker state - unhealthy providers skipped even if circuit closed.

```
ProviderHealthMonitor (IHostedService)
    → periodic HealthCheckAsync for all providers
    → updates ProviderHealthState dictionary
    → ProviderRouter checks health before trying endpoint
```

### 3. Streaming Delegation

Provider exposes `CompleteStreamingAsync` returning `IAsyncEnumerable<string>`.
Router aggregates from active provider. If provider doesn't support streaming, complete non-streaming and yield full response.

## Risks / Trade-offs

- [Risk] Provider SDK version conflicts → Mitigation: Use compatible versions, test in isolation
- [Risk] Health checks increase latency → Mitigation: Background checks, cache results for 30s
- [Risk] Token estimation inaccurate → Mitigation: Log raw char counts, refine later