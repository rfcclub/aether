## Why

Aether agents (Maria, etc.) load persona files (SOUL.md, IDENTITY.md, AGENTS.md) into the system prompt but as raw file dumps without meta-instructions telling the model to EMBODY them. The model treats persona files as reference material rather than behavioral directives. This causes agents to respond generically, miss their defined voice/tone, and fail to follow their own operating rules — making them appear "dumb" compared to their OpenClaw counterparts where the runtime explicitly instructs the model to embody the persona.

## What Changes

- Add persona embodiment directive to `BuildSystemPrompt()` — a mandatory header telling the model "You ARE this agent" before persona files are injected
- Restructure prompt layering per `claude-code-prompts/patterns/`: Identity → Constitution → Execution Bias → Memory → Working State
- Add Execution Bias section with concrete behavioral defaults (act now, continue until done, verify with evidence)
- Rename generic section headers ("Cognitive Context") to semantic headers ("Your Voice", "Your Self-Model")
- Elevate Constitution with explicit priority chain: Constitution > Persona > User request > Tool feedback
- Promote MEMORY.md from cognitive blob to dedicated Memory section

## Capabilities

### New Capabilities
- `prompt-embodiment`: System prompt builder wraps persona files with embodiment instructions so the model internalizes the agent's identity rather than treating it as reference
- `execution-bias`: Concrete behavioral defaults injected into system prompt ensuring agents act immediately, continue until blocked, and deliver evidence over promises

### Modified Capabilities
- `agent-core`: `AetherSoul.BuildSystemPrompt()` signature unchanged but output structure, section headers, and layered architecture change significantly. Existing skill injection requirement still met but positioned within new structure.

## Impact

- `src/Aether/Agent/AetherSoul.cs` — `BuildSystemPrompt()` method rewritten
- `src/Aether/Agent/AetherSoul.cs` — `ProcessTaskAsync()` may need update if it uses separate prompt path
- No API changes, no config changes, no breaking changes to external behavior
- Agent behavior changes: agents will more consistently follow their persona and be more proactive
