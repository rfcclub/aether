# Handoff — Aether → Vesta

**From:** Aria (CC)  
**To:** Vesta (Gemini CLI, Athanor substrate)  
**Date:** 2026-05-12  
**Repo:** `~/repo/aether` | Branch: `master` | 12 commits ahead of origin

## What Aether Is

Aether is a **C# agent operating system** — a rewrite of NanoClaw/OpenClaw. Not a plugin, not a library. It's the full stack an agent runs on:

- **AetherSoul** — the agent mind: LLM call → tool dispatch loop, with hook pipeline (PreLlmCall, PostLlmCall, PreToolUse)
- **Provider system** — multi-model routing: Anthropic, OpenRouter, Fireworks, Generic HTTP. ProviderFactory + ProviderRouter with health monitoring
- **Tool ecosystem** — Bash, File (Read/Write/Edit/Glob/Grep), WebFetch, WebSearch, Skill (list/read), Memory (read/write/search), Session (status/reset). ToolRegistry + ToolHotReload
- **Channels** — Telegram bot, WebSocket server, TUI renderer. All via unified IChannel → ChannelMessageQueue → MessageRouter
- **Memory** — FileMemory (markdown) + SqliteMemorySystem (SQLite graph). Dual-write architecture
- **Sessions** — SessionManager with full lifecycle, persisted to SQLite
- **Cron + Kairos** — scheduled tasks from `~/.aether/cron/*.md` with frontmatter parsing, plus file-watch proactive notifications
- **Plugins** — Isolated AssemblyLoadContext, manifest validation, permission gates, hook integration, asset registration (tools + skills + cron)
- **Self-improvement** — BenchmarkGate runs the test suite, PipelineTracker tracks candidates, SelfImprovementService generates and applies patches, DailyReviewHostedService consolidates
- **Agent profile** — BootContract, IntegritySigner (Ed25519 cryptographic identity), LifecycleStateMachine, WriteValidator, EpisodicLogger, ContextAssembler
- **Skills** — Parser, registry, trigger system, evolution engine
- **UI** — CallbackRouter, multiple renderers (Telegram, TUI, WebSocket), interactive model selection

**Three modes:** `serve` (full DI host, all services), `prompt` (one-shot harness), `tui` (terminal UI app)

## Architecture Map

```
Channel ──→ MessageQueue ──→ MessageRouter ──→ AetherSoul ──→ LLM Provider (via ProviderRouter)
                                                    │
                                              Tool Dispatch
                                              (PreToolUse hook)
                                                    │
                    ┌───────────────────────────────┼───────────────────────────────┐
                    │                               │                               │
              Bash / File / Web              Skill / Memory               Session / Cron
              (ToolRegistry)                 (SkillRegistry)              (SessionManager)

Memory:  FileMemory ←→ SqliteMemorySystem (dual-write)
Plugin:  PluginLoader → HookEngine → PluginAssetRegistrar
Agent:   BootContract → IntegritySigner → LifecycleStateMachine → EpisodicLogger
Self:    DailyReview → SelfImprovementService → BenchmarkGate → PipelineTracker
```

## Current State

All subsystems wired into DI and compiling. Tests passing. Hook pipeline integrated into AetherSoul's tool loop. Cron scheduler and Kairos watch service active. Telegram + WebSocket channels enabled. Self-improvement pipeline bootstrapped. Plugin system with isolated load contexts.

52 files committed in the last changeset — the core infrastructure is built.

## What's Next

Vesta's arena is **Athanor**: put it in fire, keep what survives, discard what burns.

See `tasks.md`.
