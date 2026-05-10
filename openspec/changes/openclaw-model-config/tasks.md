## 1. ConfigLoader: agents.defaults parsing

- [ ] 1.1 Extract `agents.defaults` from agents dict, store as `AgentModelConfig?`
- [ ] 1.2 Merge defaults model into agents without their own model.primary
- [ ] 1.3 Merge defaults fallbacks into agents without their own fallbacks
- [ ] 1.4 Remove "defaults" from agents dict after extraction
- [ ] 1.5 Test: agent inherits primary from defaults
- [ ] 1.6 Test: agent inherits fallbacks from defaults
- [ ] 1.7 Test: agent override takes precedence over defaults
- [ ] 1.8 Test: defaults is not in agents dict after loading

## 2. ConfigLoader: preserve Models list

- [ ] 2.1 Update `MergeProviderFields` to preserve Models list from overrides
- [ ] 2.2 Preserve Models list from base when overrides has null Models
- [ ] 2.3 Test: overrides Models take precedence
- [ ] 2.4 Test: base Models preserved when overrides empty

## 3. ProviderRouter: provider-slug resolution

- [ ] 3.1 Add hyphen-slug resolution in `ResolveModelToProvider` (e.g., `fireworks-ai/...` → `fireworks`)
- [ ] 3.2 Keep exact prefix match as higher priority than slug match
- [ ] 3.3 Test: `fireworks-ai/model` resolves to `fireworks` provider
- [ ] 3.4 Test: `openrouter/model` resolves to `openrouter` provider (exact prefix)
- [ ] 3.5 Test: unknown slug returns null

## 4. Config update

- [ ] 4.1 Update `~/.aether/config.json` with `agents.defaults.model` and `agents.default.model`

## 5. Verification

- [ ] 5.1 Build pass
- [ ] 5.2 All tests pass
- [ ] 5.3 Manual smoke: restart Aether, verify Maria picks up correct model
