# Proposal: Refactor Working Context

## Summary

Replace Aether's multi-layer context assembly (ContextAssembler, BootContract, 7-layer system prompt) with a minimal WorkingContext pattern. One system prompt built once. Message history in memory. No disk reload per turn. De-async file I/O, config, memory operations.

## Motivation

Aether's agent couldn't maintain context across turns because every `ProcessAsync` call rebuilt the system prompt from disk files (AGENTS.md, SOUL.md, MEMORY.md, daily memory, working state, group context). The 7-layer context assembly caused persona drift and startup rituals that overrode task continuity.

NanoClaw/PicoClaw proved the correct pattern: the conversation IS the context. One system prompt, message history in RAM, no startup rituals.

## Scope

### In
- `WorkingContext` class: system prompt + message history + tools
- De-async: file I/O, config loading, memory operations (sync by default)
- Async kept only for: LLM API calls
- Clean separation: Aether = working context, OpenClaw = agent persona

### Out
- ContextAssembler removal (kept for backward compat, marked obsolete)
- Persona/identity management (belongs to OpenClaw)
