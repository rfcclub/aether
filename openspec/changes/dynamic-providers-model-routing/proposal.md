## Why

Maria (default agent) migrated to the new Aether system but cannot read files in her workspace at `/home/thoor/.aether/workspaces/default` because sandbox `allowed_paths` in `appsettings.json` point to non-existent `/workspace/group` and `/workspace/global` paths. Separately, thoor tried registering `crof.ai` as a provider via `config.json` but hit errors because provider instantiation is hardcoded in `Program.cs` for only three providers (OpenRouter, Fireworks, Anthropic). The provider-priority routing model no longer fits: agents should work with model lists (primary + fallbacks) sourced from any registered provider, not be locked into a provider-first ordering.

## What Changes

- **Remove sandbox path restrictions on workspace access**: Allow agents to read/write files within their own workspace and any paths configured per-agent in `.aether.json`. Keep the sandbox infrastructure but make `allowed_paths` default to the agent's workspace directory when not explicitly configured. **BREAKING**: Removes `appsettings.json` sandbox `allowed_paths` as the sole source of truth; the workspace path becomes the default allowed path.
- **Dynamic provider registration from config.json**: Read all providers from `~/.aether/config.json` (and `appsettings.json`) and instantiate appropriate `ILLMProvider` implementations dynamically at startup. Remove hardcoded OpenRouter/Fireworks/Anthropic singleton registrations in `Program.cs`. If no provider config exists, log error and skip. **BREAKING**: Removes hardcoded defaults for provider models.
- **Model-based routing instead of provider-based**: Change the priority system from "which provider first" to "which model first." Each agent gets a `model.default` and `model.fallbacks` list. The router resolves each model name to a provider (by matching model prefix or explicit config) and tries them in order. Provider-level priorities become a secondary tiebreaker when multiple providers can serve the same model.
- **Integration tests**: Add integration tests that verify end-to-end: workspace file access, dynamic provider loading from config.json, and model fallback routing with a real (or faked) provider chain.

## Capabilities

### New Capabilities
- `dynamic-provider-registration`: Read provider definitions from config.json and appsettings.json, instantiate ILLMProvider implementations dynamically at startup without hardcoded registrations in Program.cs
- `model-first-routing`: Route requests based on per-agent model lists (primary + fallbacks) rather than provider priority ordering, resolving model names to providers at call time
- `workspace-sandbox-integration`: Default sandbox allowed_paths to the agent's own workspace directory, with per-agent overrides from .aether.json

### Modified Capabilities
- *(none — no existing specs to modify; this is the first spec-driven change for these subsystems)*

## Impact

- **`src/Aether/Program.cs`**: Remove ~70 lines of hardcoded provider registration (lines 547-619), replace with dynamic registration loop over config-loaded providers
- **`src/Aether/Config/ConfigLoader.cs`**: Already reads providers from config.json; may need minor additions for model list resolution
- **`src/Aether/Providers/ProviderRouter.cs`**: Refactor `CompleteAsync` to route by model chain rather than provider group priority; add model→provider resolution
- **`src/Aether/Agent/ToolExecutor.cs`**: Change `IsPathAllowed()` to default to the agent workspace path when no explicit allowed_paths are configured
- **`src/Aether/appsettings.json`**: Update sandbox section (remove hardcoded `/workspace/*` paths)
- **`tests/Aether.Tests/`**: Add integration tests for the three new capabilities
