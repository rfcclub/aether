## Context

Aether currently has three providers hardcoded in `Program.cs` (OpenRouter, Fireworks, Anthropic) registered as singletons with explicit DI. Providers from `config.json` are parsed by `ConfigLoader` but never instantiated — only the three hardcoded ones exist at runtime. The `ProviderRouter` routes by provider group priority (fireworks > anthropic > openrouter), not by model. Maria's `.aether.json` specifies `"primary": "crof-ai/glm-5.1"` with fallback `"google/gemini-3.1-pro-preview"` but these models can't be used because no provider for `crof.ai` exists.

The sandbox has `allowed_paths` hardcoded in `appsettings.json` to `/workspace/group` and `/workspace/global` — paths that don't exist. Maria's actual workspace at `/home/thoor/.aether/workspaces/default` is blocked by `ToolExecutor.IsPathAllowed()`.

**Constraints:**
- Must work with existing `OpenAiCompatibleProviderBase` and `AnthropicCompatibleProviderBase` base classes
- Must support `GenericHttpProvider` for arbitrary OpenAI-compatible endpoints
- Must not break existing Telegram channel integration
- Must use TDD with integration tests per thoor's explicit requirement

## Goals / Non-Goals

**Goals:**
- Dynamically register any number of providers from `config.json` + `appsettings.json` without recompiling
- Route by per-agent model list (primary → fallback[0] → fallback[1]...) instead of provider priority
- Resolve model names to providers via prefix matching and explicit mapping
- Default sandbox to agent workspace path so agents can read/write their own files
- Integration tests for provider loading, model routing, and workspace access

**Non-Goals:**
- Hot-reload of provider config at runtime (still requires restart)
- Provider health monitoring changes (existing circuit breaker stays)
- Removing sandbox infrastructure entirely (keep SandboxOptions, just change defaults)
- Plugin system for custom provider DLLs (config.json providers only)
- Migration tool for existing hardcoded configs (backward compat via same config keys)

## Decisions

### Decision 1: Provider Factory pattern with type-based instantiation

**Chosen**: A `ProviderFactory` class reads `SpecProviderEntry.Type` and instantiates the correct implementation:
- `"openai"` → `GenericHttpProvider` (wraps any OpenAI-compatible endpoint via `OpenAiCompatibleProviderBase`)
- `"anthropic"` → `AnthropicCompatibleProvider` (wraps any Anthropic Messages API-compatible endpoint via `AnthropicCompatibleProviderBase`) — NOT specific to api.anthropic.com; any endpoint speaking Anthropic's API protocol uses this type
- `"openrouter"` → `OpenRouterProvider` (subtype of OpenAI with OpenRouter-specific auth headers)

**Alternative considered**: Storing `Type` as a fully-qualified CLR type name for dynamic activation. Rejected — over-engineered for the current need and introduces security concerns with `Activator.CreateInstance`.

**Rationale**: The `type` field describes the API protocol, not the vendor. `"openai"` means "OpenAI Chat Completions API shape" — covers openrouter, fireworks, crof.ai, deepseek, groq, x.ai, etc. `"anthropic"` means "Anthropic Messages API shape" — covers api.anthropic.com, any Anthropic-compatible proxy, or self-hosted Anthropic-compatible endpoints. Documentation with examples is sufficient; no need for per-vendor subclasses.

### Decision 2: Model → Provider resolution via prefix matching + explicit mapping

**Chosen**: Model names like `"crof-ai/glm-5.1"` or `"google/gemini-3.1-pro-preview"` use prefix matching against registered provider names. `"crof-ai"` prefix matches a provider named `"crof-ai"`. Additionally, `SpecProviderEntry` gains an optional `"models"` list field — if specified, the provider explicitly claims those models.

**Alternative considered**: Configuring `provider` per model in `.aether.json`. Rejected — adds friction; the prefix convention (`provider/model`) already matches OpenRouter's naming scheme and most providers follow it.

**Rationale**: This is how OpenRouter works (`provider/model`). Providers that don't follow this convention can use explicit `"models"` mapping in their config.

### Decision 3: Configurable sandbox with workspace as default

**Chosen**: Sandbox is fully configurable per agent. `ToolExecutor` receives agent workspace path AND per-agent config from `.aether.json`. Three-tier path resolution:
1. Agent's own workspace is always allowed (unless explicitly denied)
2. `tools.file.allowedPaths` in `.aether.json` adds extra paths
3. `tools.file.deniedPaths` in `.aether.json` restricts paths (even within workspace)
4. `appsettings.json` sandbox section provides global defaults that agents can override
5. Sandbox type `"none"` disables path restrictions entirely (all paths allowed)

**Alternative considered**: Hardcoding workspace as the only allowed path. Rejected — thoor wants configurable sandbox, not a different hardcode.

**Rationale**: Different agents need different access levels. A development agent might need access to `/home/thoor/repo/`. A chat agent should be confined to its workspace. Type `"none"` provides an escape hatch for trusted agents.

### Decision 4: Remove hardcoded provider registration, use config-only

**Chosen**: `Program.cs` reads all configured providers from `ConfigLoader`, creates instances via `ProviderFactory`, and registers them as `ILLMProvider` singletons. If `config.json` has no providers configured, log a warning and continue with zero providers (agent will fail with clear error if trying to route).

**Alternative considered**: Keep hardcoded defaults as fallback. Rejected — thoor explicitly wants config-only with error if missing.

**Rationale**: Single source of truth. No silent fallback to hardcoded defaults that mask config errors.

## Risks / Trade-offs

- **Startup order dependency**: `ConfigLoader` must be registered and `LoadAsync()` called before provider registration. → Mitigation: `ConfigLoader` is already registered early in `Program.cs`; move provider registration to after config loading.
- **Prefix matching ambiguity**: Model `"mistral"` could match both a provider named `"mistral"` and one named `"mistral-large"`. → Mitigation: Sort providers by name length descending (longest match first).
- **GenericHttpProvider no longer has defaults**: Previously each hardcoded provider had a fallback model. → Mitigation: Config validation logs warnings for providers without models.
- **Sandbox widening**: Making workspace fully accessible removes file isolation. → Mitigation: Per-agent `tools.file.deniedPaths` and `tools.file.allowedPaths` provide fine-grained control. Type `"none"` should only be used for trusted agents.

## Migration Plan

1. Run existing tests to establish baseline
2. Implement `ProviderFactory` with `GenericHttpProvider` support
3. Refactor `Program.cs` to load providers dynamically
4. Update `ProviderRouter` for model-first routing
5. Update `ToolExecutor` for workspace-default sandbox
6. Run full test suite; fix regressions
7. Deploy — thoor updates `config.json` with any additional providers

**Rollback**: No database migration involved. Revert to previous commit to restore hardcoded behavior.

## Open Questions

- Should `config.json` `provider_priorities` still be used when multiple providers can serve the same model? → Yes, as tiebreaker when model resolved to multiple providers. But this is a secondary concern; model fallback chain is primary.

## Decision 5: Persistent model selection via /model slash command

**Chosen**: `/model` (no args) lists available models from all registered providers with current model marked. `/model <model-id>` validates against ProviderRouter, updates the in-memory ModelChain, and persists the primary model to `config.json` under `agents.<name>.model.primary` using a JSON-patching `UpdateAgentModelAsync` method on ConfigLoader. On next startup, ChannelMessageProcessor reads the persisted model from agent config and sets ModelChain accordingly.

**Alternative considered**: Store model choice in session state only. Rejected — thoor wants model selection to survive restart, like OpenClaw.

**Rationale**: `config.json` is the single source of truth for agent config. Writing the model choice there means zero additional state files, and the model survives any restart/crash without extra infrastructure.
