## ADDED Requirements

### Requirement: Periodic health checks
`ProviderHealthMonitor` SHALL run as `IHostedService` and call `HealthCheckAsync` on each registered provider every 30 seconds.

#### Scenario: Health check succeeds
- **WHEN** provider returns `true` from `HealthCheckAsync`
- **THEN** the monitor SHALL update provider state to `Healthy` and log success

#### Scenario: Health check fails
- **WHEN** provider throws or returns `false`
- **THEN** the monitor SHALL update provider state to `Unhealthy`, increment failure count, and log warning

### Requirement: Integration with circuit breaker
Provider health status SHALL influence `ProviderRouter` circuit breaker decisions.

#### Scenario: Unhealthy provider skipped
- **WHEN** `ProviderRouter.SelectEndpoint` is called and provider is `Unhealthy`
- **THEN** the router SHALL skip that provider regardless of circuit breaker state

### Requirement: Health state persistence
Provider health state SHALL be stored in-memory with TTL. After 3 consecutive failures, provider marked `Degraded`.

#### Scenario: Provider recovers
- **WHEN** unhealthy provider passes health check
- **THEN** the monitor SHALL reset failure count and restore `Healthy` state