## 1. Workspace sandbox — allow workspace access

- [x] 1.1 Write integration test: agent can read file in own workspace directory
- [x] 1.2 Write integration test: agent can write file in own workspace subdirectory
- [x] 1.3 Write integration test: agent blocked from reading file outside workspace
- [x] 1.4 Write integration test: per-agent allowed_paths from .aether.json are honored
- [x] 1.5 Update `ToolExecutor` to accept agent workspace path parameter in constructor
- [x] 1.6 Update `AetherSoul` (or `ToolExecutor` DI registration) to pass workspace path to ToolExecutor
- [x] 1.7 Update `appsettings.json` sandbox section: remove hardcoded `/workspace/group`, `/workspace/global` (or leave empty; workspace becomes default)
- [x] 1.8 Write integration test: sandbox type "none" allows all paths (including /etc/passwd)
- [x] 1.9 Write integration test: per-agent denied_paths block access to specific subdirectories even within workspace
- [x] 1.10 Update `SandboxOptions` to support `"none"` type that skips all path validation
- [x] 1.11 Update `ToolExecutor` to accept per-agent `SpecToolsSection` config and apply allowedPaths/deniedPaths from .aether.json

## 2. Provider factory — dynamic instantiation from config

- [x] 2.1 Create `ProviderFactory` class with `ILLMProvider Create(SpecProviderEntry entry, string providerName)` method
- [x] 2.2 Implement type dispatch: `"openai"` → `GenericHttpProvider`, `"anthropic"` → `AnthropicCompatibleProvider` (any Anthropic Messages API endpoint, not specific to api.anthropic.com), unknown → `GenericHttpProvider` with warning
- [x] 2.3 Write unit test: factory creates GenericHttpProvider for openai type
- [x] 2.4 Write unit test: factory creates AnthropicCompatibleProvider for anthropic type (with custom base URL)
- [x] 2.5 Write unit test: factory creates GenericHttpProvider for unknown type with warning

## 3. Dynamic provider registration in Program.cs

- [x] 3.1 Write integration test: providers from config.json are registered as ILLMProvider singletons
- [x] 3.2 Write integration test: zero providers registered when config.json has no providers section (warning logged)
- [x] 3.3 Refactor `Program.cs`: remove hardcoded OpenRouterOptions/FireworksOptions/AnthropicOptions singletons (lines 547-595)
- [x] 3.4 Refactor `Program.cs`: remove hardcoded ILLMProvider registrations (lines 555-560, 575-580, 590-595)
- [x] 3.5 Refactor `Program.cs`: add dynamic registration loop — load providers from ConfigLoader, create via ProviderFactory, register as singletons
- [x] 3.6 Verify existing `ProviderRouter` still works with dynamically registered providers (constructor receives IEnumerable<ILLMProvider>)

## 4. Model-first routing in ProviderRouter

- [x] 4.1 Write integration test: agent with primary + fallback models uses primary first
- [x] 4.2 Write integration test: agent falls back to second model when primary fails
- [x] 4.3 Write integration test: agent falls back through all models in chain
- [x] 4.4 Write integration test: model resolved by prefix match (e.g., `"openrouter/deepseek/..."`)
- [x] 4.5 Write integration test: model resolved by explicit model list in provider config
- [x] 4.6 Write integration test: unresolvable model is skipped with warning
- [x] 4.7 Add `ResolveModelToProvider(modelId)` method to `ProviderRouter`
- [x] 4.8 Refactor `CompleteAsync` to accept model chain and iterate models with fallback
- [x] 4.9 Update `ChannelMessageProcessor` (or wherever `CurrentAgent` is set) to pass agent model chain to ProviderRouter
- [x] 4.10 Update `SpecContracts.cs`: add optional `List<string> Models` to `SpecProviderEntry`
- [x] 4.11 Update `ConfigLoader` to parse `"models"` array from provider entries

## 5. Integration tests — end-to-end

- [x] 5.1 Write end-to-end test: Maria reads SOUL.md from her workspace via ToolExecutor (no sandbox block)
- [x] 5.2 Write end-to-end test: config.json providers are loaded and callable
- [x] 5.3 Write end-to-end test: agent model fallback chain works with a FakeLlmProvider that fails first call then succeeds
- [x] 5.4 Run full test suite, fix any regressions in existing tests that depend on hardcoded provider registration
- [x] 5.5 Verify Telegram channel still routes correctly with dynamic providers

## 6. Cleanup

- [x] 6.1 Remove unused Options classes (`OpenRouterOptions`, `FireworksOptions`, `AnthropicOptions`) if no longer needed
- [x] 6.2 Remove unused Provider subclasses if covered by `GenericHttpProvider` (keep `OpenRouterProvider` only if it adds unique headers; otherwise generic handles it)
- [x] 6.3 Update `appsettings.json` comments/documentation for new config-driven provider model
- [x] 6.4 Run `dotnet test` — all tests green

## 7. Persistent /model slash command

- [x] 7.1 `/model` (no args) lists available models from all providers with current model marked
- [x] 7.2 Add `GetAvailableModels()` to ProviderRouter returning (provider, model) pairs
- [x] 7.3 `/model <model-id>` validates against known models, rejects unknown with available list
- [x] 7.4 Add `UpdateAgentModelAsync` to ConfigLoader — JSON-patches `agents.<name>.model.primary` in config.json
- [x] 7.5 Inject `ConfigLoader` into `SlashCommandHandler` for persistence
- [x] 7.6 On next startup, ChannelMessageProcessor reads model from agent config → ModelChain (already wired)
- [x] 7.7 Update slash command tests for new model listing/rejection behavior
