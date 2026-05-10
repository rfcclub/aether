## Context

OpenClaw's `openclaw.json` uses a two-level agent model config:

```json
{
  "agents": {
    "defaults": {
      "model": {
        "primary": "fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo",
        "fallbacks": ["google/gemini-2.5-flash-lite", "openrouter/deepseek/deepseek-r1:free"]
      }
    },
    "maria": {
      "name": "maria",
      "workspace": "...",
      "model": { "primary": "fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo" }
    }
  }
}
```

Key behaviors:
- `defaults` is a special key — not a real agent, just a shared model baseline.
- An agent without `model.primary` inherits from `defaults.model.primary`.
- An agent without `model.fallbacks` inherits from `defaults.model.fallbacks`.
- Model identifiers use `provider-slug/model-id` format (e.g., `fireworks-ai/...`).
- Provider slugs map to registered providers: `fireworks-ai` → `fireworks`, `openrouter` → `openrouter`, `google` → `openrouter` (with provider routing).

Aether currently:
- Has no `defaults` concept — agents are parsed individually.
- Loses the `Models` list during provider merge (`MergeProviderFields` ignores it).
- Uses `ResolveModelToProvider()` which only does exact match + `prefix/model` split on the literal provider name.

## Goals / Non-Goals

**Goals:**

- Parse `agents.defaults` from `~/.aether/config.json` as a shared model baseline.
- Merge defaults into agents that don't override model config.
- Preserve `Models` list through provider merge.
- Map provider slugs in model identifiers to registered providers.
- Model chain cascade: agent primary → agent fallbacks → defaults primary → defaults fallbacks → provider default.

**Non-Goals:**

- Change `AgentSpecConfig` or `SpecContracts.cs` — types already support what we need.
- Change channel bindings or per-agent workspace resolution.
- Add new config validation (can be a follow-up).
- Add provider slug aliases like `google` → `openrouter` (follow-up).

## Design

### 1. Parse `agents.defaults` in ConfigLoader

In `LoadGlobalConfigAsync`, after parsing agents, extract `defaults` as a special entry:

```csharp
AgentModelConfig? defaultsModel = null;
if (agents.TryGetValue("defaults", out var defaultsEntry))
{
    defaultsModel = defaultsEntry.Model;
    agents.Remove("defaults"); // not a real agent
}
```

Store it in the return tuple.

### 2. Merge defaults into agents

After loading all agents, for each agent that has no model (or has partial model), inherit from defaults:

```csharp
if (defaultsModel is not null)
{
    foreach (var (name, entry) in agents)
    {
        if (string.IsNullOrEmpty(entry.Model.Primary) && !string.IsNullOrEmpty(defaultsModel.Primary))
            agents[name] = entry with { Model = entry.Model with { Primary = defaultsModel.Primary } };
        if (entry.Model.Fallbacks.Count == 0 && defaultsModel.Fallbacks.Count > 0)
            agents[name] = agents[name] with { Model = agents[name].Model with { Fallbacks = new List<string>(defaultsModel.Fallbacks) } };
    }
}
```

### 3. Preserve `Models` list in MergeProviderFields

```csharp
Models = overrides.Models is { Count: > 0 } ? overrides.Models : base_.Models,
```

### 4. Provider-slug model resolution

Add a provider slug mapping in `ProviderRouter.ResolveModelToProvider`:

```csharp
// "fireworks-ai/accounts/fireworks/..." → try provider "fireworks"
var hyphenIdx = modelId.IndexOf('/');
if (hyphenIdx > 0)
{
    var slug = modelId[..hyphenIdx]; // "fireworks-ai"
    var baseName = slug.Replace("-ai", "").Replace("-llm", ""); // "fireworks"
    var match = _providers.FirstOrDefault(p =>
        p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
    if (match is not null) return match;
}
```

### 5. Model chain cascade in ChannelMessageProcessor

The existing code already builds model chain from `agentConfig.Model`:

```csharp
if (agentConfig?.Model is { } modelCfg)
{
    var chain = new List<string>();
    if (!string.IsNullOrEmpty(modelCfg.Primary))
        chain.Add(modelCfg.Primary);
    chain.AddRange(modelCfg.Fallbacks);
    ...
}
```

After defaults merge, this will automatically include inherited fallbacks. No change needed in ChannelMessageProcessor.

## Risks / Trade-offs

- **Provider slug ambiguity**: `fireworks-ai` → `fireworks` is heuristic. Could misroute if a provider is named `fireworks-ai` explicitly. Mitigated by checking exact match first.
- **Defaults is not a real agent**: Removing it from the agents dict means it cannot be targeted by name. This is intentional — defaults is config, not a runtime agent.
- **Breaking change**: If someone has an agent literally named "defaults", it will be treated as a shared baseline and removed. Low risk — "defaults" is an OpenClaw convention.

## Rollout

1. Implement defaults parsing + merge in ConfigLoader.
2. Preserve Models list in MergeProviderFields.
3. Add provider-slug resolution in ProviderRouter.
4. Update `~/.aether/config.json` with OpenClaw-compatible format.
5. Tests for all above.
6. Manual smoke: restart Aether, verify Maria picks up correct model.
