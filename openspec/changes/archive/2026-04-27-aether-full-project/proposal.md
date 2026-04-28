## Why

Aether needs a complete agent runtime that can receive messages from external channels, maintain bounded persistent memory, execute tools safely, and route LLM calls intelligently — none of these pieces are fully wired end-to-end yet. The skeleton is in place; this change finishes every layer and ships a running agent.

## What Changes

- **Complete Memory System**: SQLite FTS5 schema, BM25 search, smart compaction, and promotion pipeline from ephemeral → working → durable (MEMORY.md)
- **Complete LLM Provider Router**: Complexity scoring, response confidence estimation, circuit breaker, streaming, and cost tracking across Fireworks → OpenRouter → Anthropic tiers
- **Complete Tool System**: JSON Schema validation (NJsonSchema), permission model (per-agent allowlists), hot-reload via FileSystemWatcher
- **Build Gateway**: WebSocket channel as primary entry point; Telegram channel adapter; normalized `InboundMessage` → `AetherSoul` wiring
- **Build Skill System**: SKILL.md parser (frontmatter + body), directory loader, trigger detection (description similarity + explicit `/skill-name`), prompt injection
- **Build Self-Improvement Workflow**: 6-phase pipeline (daily review → promotion → skill evolution → visibility → recidivism enforcement → cross-layer optimization), no-auto-commit, benchmark gating
- **Wire Host Startup**: `AetherDb.InitializeAsync()` at startup, `SqliteMemorySystem.InitializeAsync()`, DI cleanup (duplicate `IToolExecutor` registration removed)
- **Multi-Agent Gateway Routing**: Single gateway, multiple named agents selected by channel/config (OpenClaw-style model A)

## Capabilities

### New Capabilities

- `gateway`: External entry point — WebSocket + Telegram channels, normalized message routing to AetherSoul
- `skill-system`: Procedural capabilities from SKILL.md files — parser, loader, trigger detection, prompt injection
- `self-improvement`: 6-phase recursive improvement pipeline — daily review, promotion, skill evolution, recidivism enforcement
- `multi-agent`: Gateway-level routing to multiple named agent instances with isolated sessions

### Modified Capabilities

- `memory-system`: Completing skeleton — FTS5 schema, BM25 search, smart compaction algorithm, promotion pipeline, host initialization
- `llm-router`: Completing skeleton — complexity scoring, confidence estimation, circuit breaker, streaming support, cost tracking
- `tool-system`: Completing skeleton — NJsonSchema validation, permission model, hot-reload via FileSystemWatcher
- `agent-core`: Completing integration — gateway wiring, streaming response support, memory context injection

## Impact

- `src/Aether/Memory/SqliteMemorySystem.cs` — full implementation
- `src/Aether/Providers/ProviderRouter.cs` — complexity/confidence/circuit-breaker
- `src/Aether/Tooling/ToolRegistry.cs`, `ToolExecutor.cs` — schema validation, permissions
- `src/Aether/Channels/` — WebSocket + Telegram adapters (new)
- `src/Aether/Skills/` — entire new namespace
- `src/Aether/Improvement/` — entire new namespace
- `src/Aether/Program.cs` — startup wiring, DI fix
- `src/Aether/Aether.csproj` — NJsonSchema, WebSockets packages
