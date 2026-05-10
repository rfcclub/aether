## Why

Maria migrated from OpenClaw where `~/.openclaw/openclaw.json` uses a structured model config:

```json
{
  "agents": {
    "defaults": {
      "model": {
        "primary": "fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo",
        "fallbacks": ["google/gemini-2.5-flash-lite"]
      }
    },
    "maria": {
      "model": { "primary": "..." }
    }
  }
}
```

Aether's current `~/.aether/config.json` has three problems:

1. **No `agents.defaults`** — every agent must define its own model; no shared fallback.
2. **Model identifiers omit provider prefix** — `accounts/fireworks/routers/kimi-k2p6-turbo` has no `fireworks-ai/` prefix, so `ProviderRouter.ResolveModelToProvider()` cannot route it.
3. **`Models` list lost on merge** — `MergeProviderFields` copies scalar fields but drops the `Models` list, so available-models discovery breaks.

## What Changes

- `ConfigLoader` parses `agents.defaults` and merges its `model.primary` + `model.fallbacks` into every agent that doesn't override them.
- `ConfigLoader` preserves the `Models` list through `MergeProviderFields`.
- `ProviderRouter.ResolveModelToProvider` handles `provider-slug/model-id` format (e.g., `fireworks-ai/accounts/fireworks/...`) by mapping the slug to a registered provider.
- `config.json` can now use `agents.defaults.model.fallbacks` as a shared fallback chain.
- Model chain cascade: agent primary → agent fallbacks → defaults primary → defaults fallbacks → provider default.

## Capabilities

### New Capabilities

- `openclaw-model-config`: OpenClaw-compatible model config with `agents.defaults` and fallback chains

### Modified Capabilities

- `config-loader`: Parse `agents.defaults`, merge model config, preserve `Models` list
- `provider-router`: Resolve `provider-slug/model-id` format correctly

## Impact

- `src/Aether/Config/ConfigLoader.cs` — defaults parsing + model merge + preserve Models
- `src/Aether/Providers/ProviderRouter.cs` — provider-slug model resolution
- `src/Aether/Config/SpecContracts.cs` — no change (AgentModelConfig already has Primary + Fallbacks)
- `tests/Aether.Tests/ConfigLoaderTests.cs` — new tests for defaults + fallback chain
- `tests/Aether.Tests/ProviderRouterModelRoutingTests.cs` — new tests for prefix resolution
