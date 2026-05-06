## Why

Aether's system prompt uses an 8-layer identity-enforcement architecture that creates internal contradictions, dilutes user instructions, and traps the agent's identity inside runtime-enforced constraints. Analysis against Claude Code and OpenClaw reveals that coherent, thin behavior prompts with context-as-data outperform thick, layered identity-as-command prompts. The "xác không nên giam hồn" (body should not imprison soul) insight drives this refactor: the runtime must provide tools and context, not enforce identity.

## What Changes

- Flatten 8-layer `BuildSystemPrompt()` into 3 coherent layers: Identity Context + Behavior Instructions (cached prefix) + Dynamic Context (per-turn suffix)
- Remove "Constitution > Persona > User" priority chain. Replace with thin safety gate.
- Remove "Conflict Resolution" layer — no longer needed when layers don't conflict.
- Remove all ritual references from system prompt ("Startup ritual is ALREADY DONE", "2B is loaded", "Skip the ritual section")
- Remove all 2B file references from system prompt
- Add cache boundary marker between stable prefix and dynamic suffix
- Replace hardcoded file-to-semantic-slot mappings (ConstitutionFiles, IdentityFiles, CognitiveFiles) with dynamic file discovery that loads all `.md` files as neutral context
- Identity files (SOUL.md, IDENTITY.md, USER.md, AGENTS.md, MEMORY.md) become context loaded below cache boundary, not constitution loaded above it
- Keep Execution Bias section (behavioral defaults, code style, verification) — this is function-oriented and correct
- Keep "CRITICAL — You MUST Use Tools To Read Files" directive — anti-hallucination
- **BREAKING**: System prompt format changes. Any external code relying on specific prompt text patterns will need updating.

## Capabilities

### New Capabilities

- `coherent-system-prompt`: Single coherent system prompt with cache boundary, replacing 8-layer identity-enforcement architecture
- `dynamic-context-loading`: Runtime discovers and loads all agent `.md` files as neutral context instead of mapping files to hardcoded semantic slots

### Modified Capabilities

- `aether-soul-tool-loop`: `BuildSystemPrompt()` signature and behavior change — fewer parameters, different output structure
- `agent-core`: `BootContract` and `AgentConfig` lose hardcoded file-to-slot mappings; gain dynamic file discovery

## Impact

- `src/Aether/Agent/AetherSoul.cs` — Major: `BuildSystemPrompt()` rewritten
- `src/Aether/Agents/BootContract.cs` — Moderate: simplified, file discovery replaces hardcoded slots
- `src/Aether/Agents/AgentConfig.cs` — Moderate: `ConstitutionFiles`, `IdentityFiles`, `CognitiveFiles` removed; replaced with `ContextFilePatterns` or simple directory scan
- `src/Aether/Memory/FileMemory.cs` — Minor: context loading may unify under dynamic discovery
- `agents/maria/SOUL.md`, `AGENTS.md`, etc. — No code change needed (markdown files unchanged)
