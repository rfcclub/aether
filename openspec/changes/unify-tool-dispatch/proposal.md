## Why

Maria migrated from OpenClaw into Aether, but Aether exposes a much smaller and split tool surface.

Current Aether has two tool paths:

- `AetherSoul.BuiltInTools` exposes only 6 tools to the LLM: `read`, `glob`, `grep`, `bash`, `write`, `edit`.
- `Aether.Tooling.ToolRegistry` registers richer implementations, including `web_search` and `web_fetch`, but the normal AetherSoul tool loop does not expose or dispatch through that registry.

OpenClaw gave Maria a broader working environment: the Maria sandbox has 52 skill directories, global/workspace OpenClaw adds at least 14 more skill packs, and core OpenClaw includes native tools for web, sessions/subagents, messages, cron, nodes, media, PDF, TTS, image/video/music generation, and gateway operations. Aether does not need to clone all of OpenClaw immediately, but the current 6-tool surface makes Maria feel under-equipped and causes tool-availability complaints.

## What Changes

- Make `ToolRegistry` the single source of truth for tool definitions exposed to the LLM.
- Make `AetherSoul` build LLM tool definitions from registered `ToolDefinition` entries instead of hardcoded `BuiltInTools`.
- Make AetherSoul dispatch all tool calls through `Aether.Tooling.ToolExecutor`.
- Preserve compatibility for the existing six built-in tools.
- Expose already implemented registry tools: `web_search` and `web_fetch`.
- Add an OpenClaw-migration baseline set of commonly used tools:
  - `memory_read`, `memory_write`, `memory_search`
  - `skill_list`, `skill_read`
  - `session_status`, `session_reset`
  - `shell` as a compatibility alias for `bash`
  - `exec` as a compatibility alias for `bash`, gated by policy
- Add a capability audit command or test helper that reports which tools are visible to Maria at runtime.

## Capabilities

### New Capabilities

- `unified-tool-dispatch`: AetherSoul exposes and dispatches through `ToolRegistry`
- `openclaw-migration-tool-baseline`: Adds the minimum familiar tools Maria expects after OpenClaw migration
- `runtime-tool-audit`: Reports visible tools, disabled tools, and missing OpenClaw-parity candidates

### Modified Capabilities

- `tool-system`: Registry-backed tool exposure replaces AetherSoul hardcoded tool list
- `tool-executor`: Dispatch path changes from `Aether.Agent.ToolExecutor` to `Aether.Tooling.ToolExecutor`
- `skill-system`: Skills become discoverable/readable through tools, not only prompt instructions

## Impact

- `src/Aether/Agent/AetherSoul.cs`: remove hardcoded tool definitions or keep only as fallback; inject `ToolRegistry`/tool adapter; dispatch via registry executor
- `src/Aether/Tooling/ToolRegistry.cs`: expose full descriptors for LLM conversion and stable list/audit API
- `src/Aether/Tooling/ToolExecutor.cs`: become the canonical execution path; normalize results into provider tool-result messages
- `src/Aether/Tooling/ToolStartupRegistration.cs`: register compatibility aliases and migration baseline tools
- `src/Aether/Skills/`: add skill list/read tool implementations
- `src/Aether/Memory/`: add simple file/SQLite memory tool implementations, with richer memory engine integration later
- `tests/Aether.Tests/`: add tests proving visible tool count, web tools, aliases, skill tools, and dispatch parity

