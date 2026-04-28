## 1. OpenAI-Compatible Provider (Fireworks)

- [x] 1.1 Create `FireworksProvider.cs` implementing `ILLMProvider`
- [x] 1.2 Implement `CompleteAsync` using OpenAI.NET SDK with Fireworks base URL
- [x] 1.3 Implement `CompleteStreamingAsync` returning `IAsyncEnumerable<string>`
- [x] 1.4 Add tool/function calling support (convert to OpenAI format)
- [x] 1.5 Implement `HealthCheckAsync` with `/models` endpoint call

## 2. Anthropic-Compatible Provider

- [x] 2.1 Create `AnthropicProvider.cs` implementing `ILLMProvider`
- [x] 2.2 Implement `CompleteAsync` using Anthropic `/v1/messages` endpoint
- [x] 2.3 Add proper headers (x-api-key, anthropic-version)
- [x] 2.4 Implement Claude tool use via tool_use extension
- [x] 2.5 Extract token usage from response headers
- [x] 2.6 Implement `HealthCheckAsync` with ping endpoint

## 3. Provider Health Monitor

- [x] 3.1 Create `ProviderHealthMonitor.cs` as `IHostedService`
- [x] 3.2 Implement periodic health checks every 30 seconds
- [x] 3.3 Store health state in `Dictionary<string, ProviderHealthState>`
- [x] 3.4 Integrate with `ProviderRouter` to skip unhealthy providers
- [x] 3.5 Handle recovery (reset failure count on success)

## 4. Provider Registration

- [x] 4.1 Register all providers in `Program.cs` DI container
- [x] 4.2 Wire `ProviderHealthMonitor` as `IHostedService`
- [x] 4.3 Configure provider priorities (Fireworks=primary, OpenRouter=fallback, Anthropic=safety)
- [x] 4.4 Add provider-specific config sections to `appsettings.json`