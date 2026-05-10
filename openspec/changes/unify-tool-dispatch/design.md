## Context

Maria's OpenClaw environment was not just a shell. It had:

- Core coding/file tools.
- Web search/fetch.
- Skill packs for GitHub, weather, PDF, image generation, notes, reminders, Discord/Slack, session logs, voice, TTS, model usage, and more.
- Agent/session/subagent tools.
- Memory-specific skills such as `maria-memory`, `luna-memory`, `2b-memory`, and Moltnet.

Aether currently has less operational affordance:

| Surface | Count / State | Notes |
| --- | ---: | --- |
| Aether visible to LLM | 6 | `read`, `glob`, `grep`, `bash`, `write`, `edit` hardcoded in `AetherSoul` |
| Aether registry | 8+ | Adds `web_search`, `web_fetch`, hot-reload, but not exposed by AetherSoul |
| OpenClaw Maria sandbox skills | 52 | Skill-backed capabilities under `~/.openclaw/sandboxes/agent-maria-*/skills` |
| OpenClaw global/workspace skills | 14+ | Includes `maria-memory`, `moltnet`, `codex-proxy`, `research-assistant`, etc. |
| OpenClaw core native agent tools | 20+ | Web, message, sessions, subagents, cron, nodes, media, PDF, gateway, TTS |

The right migration shape is layered parity, not a bulk port.

## Goals / Non-Goals

**Goals:**

- Make every registered Aether tool visible to the model when policy allows it.
- Use one execution path so tool availability, schema validation, logging, sandboxing, and hot-reload agree.
- Restore the most common OpenClaw-era affordances Maria expects.
- Preserve safety boundaries around outbound communication, secrets, destructive filesystem actions, and owner-only controls.
- Add a visible runtime audit so Thoor can ask "what tools does Maria have?" and get a concrete answer.

**Non-Goals:**

- Port all 52 OpenClaw skills in one change.
- Implement all OpenClaw channel/media integrations natively.
- Add unrestricted external communication tools.
- Replace the separate memory-engine design; this change adds only baseline memory tools and leaves rich retrieval to memory-engine phases.

## Design

### 1. ToolRegistry Is Canonical

`ToolRegistry` should expose descriptors:

```csharp
public sealed record ToolDescriptor(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    ToolRisk Risk,
    bool Enabled);
```

`AetherSoul` asks a `ToolCatalog` or `ToolRegistry` for enabled descriptors and converts them to provider `LlmTool` objects.

The old static `BuiltInTools` list may remain temporarily as a fallback for prompt harness tests, but production runtime should use registry descriptors.

### 2. Registry Executor Is Canonical

`AetherSoul` dispatches tool calls through `Aether.Tooling.ToolExecutor`:

```csharp
var result = await _toolExecutor.ExecuteAsync(toolCall.Name, argsJson, ct);
```

Result formatting should be centralized so successful object results serialize predictably and failures include a concise, model-readable error.

The older `Aether.Agent.ToolExecutor` should either become an adapter over the registry executor or be retired after compatibility tests pass.

### 3. OpenClaw Migration Baseline

Add these first because they map directly to Maria's normal workflows:

| Tool | Purpose | Implementation |
| --- | --- | --- |
| `web_search` | current web lookup | already implemented, expose through registry |
| `web_fetch` | fetch/read URLs | already implemented, expose through registry |
| `memory_read` | read workspace memory files safely | managed file tool constrained to workspace memory paths |
| `memory_write` | append/write daily memory | managed file tool with append mode and write policy |
| `memory_search` | search memory directory / SQLite later | phase 1 grep-backed, later memory engine |
| `skill_list` | list available workspace skills | enumerate `skills/*/SKILL.md` |
| `skill_read` | read one skill body | read by skill name, path-safe |
| `session_status` | report current session/context stats | use WorkingContext/SessionManager |
| `session_reset` | reset current working context | controlled reset, mirrors slash `/reset` |
| `shell` | OpenClaw compatibility alias | maps to `bash` |
| `exec` | OpenClaw compatibility alias | maps to `bash`, policy-gated |

### 4. Later OpenClaw Parity Buckets

Do not port these until baseline is stable:

- Communication: `message`, Discord/Slack/Telegram sends, email-like tools. Require explicit external-send approval.
- Agent orchestration: `sessions_spawn`, `sessions_send`, `sessions_history`, `subagents`. Should wait for `agent-turn-isolation`.
- Automation: `cron`, `nodes`, gateway controls. Owner-only and audit-heavy.
- Media: `image_generate`, `video_generate`, `music_generate`, `tts`, `pdf`. Useful, but separate provider/config/security work.
- App integrations: GitHub, Notion, Trello, Apple Notes/Reminders, Obsidian, 1Password. Prefer skill wrappers first, native tools later.

### 5. Policy And Safety

Every tool descriptor should carry a risk/category:

- `read`: safe internal read
- `write`: workspace write
- `exec`: command execution
- `network`: web/network
- `external_send`: sends information outside the machine
- `owner_only`: host/gateway/automation controls

The first implementation may enforce a simple allowlist from `SpecToolsSection`, but descriptors should be shaped so richer approval can land later.

### 6. Runtime Audit

Add a test/helper output equivalent to:

```text
Visible tools: 13
Enabled: read, write, edit, glob, grep, bash, shell, web_search, web_fetch, memory_read, memory_write, memory_search, skill_list, skill_read
Disabled by policy: exec
Missing OpenClaw parity: message, sessions_spawn, sessions_send, cron, image_generate, pdf, tts, ...
```

This can start as a unit-test helper and later become a slash command (`/tools`) or CLI command.

## Risks / Trade-offs

- **Too many tools can confuse models**: Keep baseline tight and use categories/descriptions that are short.
- **Alias ambiguity**: `bash`, `shell`, and `exec` should share one implementation but maintain explicit policy labels.
- **Hot-reload schema quality**: Invalid schemas should not poison the prompt; disabled tools should be omitted with a logged warning.
- **External-send risk**: App/channel tools should not land in this change.
- **Migration pressure**: Maria may expect OpenClaw skill names. Prefer compatibility aliases where cheap, but do not fake tools that cannot execute.

## Rollout

1. Registry-backed exposure and dispatch for existing 8 tools.
2. Add aliases `shell` and `exec` with policy gating.
3. Add memory and skill baseline tools.
4. Add `/tools` or CLI audit.
5. Decide next parity bucket after observing Maria's failures.

