## Why

Aether's LLM router needs diverse providers for cost optimization and resilience. Currently only OpenRouter provider exists. Need OpenAI-compatible (Fireworks), Anthropic-compatible, and provider health monitoring for production reliability.

## What Changes

- Add `OpenApiCompatibleProvider` for Fireworks AI (unlimited tier)
- Add `AnthropicCompatibleProvider` for Claude via OpenRouter
- Add `FireworksProvider` as primary unlimited provider
- Add provider health monitoring with periodic checks
- Wire all providers into ProviderRouter with proper fallback

## Capabilities

### New Capabilities

- `openai-compatible-provider`: OpenAI-compatible API provider (Fireworks) with streaming support
- `anthropic-compatible-provider`: Anthropic API-compatible provider for Claude models
- `provider-health-monitor`: Periodic health checks for all registered providers with circuit breaker integration

### Modified Capabilities

- `llm-router`: Extend to support 4+ providers with health-based routing

## Impact

- New files: `Providers/FireworksProvider.cs`, `Providers/AnthropicProvider.cs`, `Providers/ProviderHealthMonitor.cs`
- Modified: `ProviderRouter.cs` (wiring), `Program.cs` (DI registration)
- Dependencies: OpenAI.NET SDK for Fireworks