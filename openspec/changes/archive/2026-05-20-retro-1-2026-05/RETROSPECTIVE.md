# Retro-1 — Identity-as-Command Architecture (2026-05-04)

## Tag

`retro-1-2026-05`

## What We Built

Aether's original system prompt architecture: 8-layer identity-enforcement model with FEOFALLS cognitive substrate and 2B boundary system.

The system prompt (`BuildSystemPrompt()` in `AetherSoul.cs`) hardcoded layers with semantic meaning:
1. Identity (Mandatory) — "You ARE this agent. Embody."
2. Current Date
3. AGENTS.md — Operating Rules
4. SOUL.md / IDENTITY.md / USER.md — Voice, Self-Model, Relationship
5. Constitution — "Non-Negotiable Red Lines" with `Constitution > Persona > User` priority chain
6. Conflict Resolution — "action follows bias, communication follows SOUL"
7. Execution Bias — behavioral defaults, code style, verification
8. Memory / Working State / Recent / Group / Skill — data injection

The runtime (`BootConfig.cs`, `AgentConfig.cs`) mapped specific files to semantic slots:
```
ConstitutionFiles = ["AGENTS_GUARD.md"]
IdentityFiles = ["SOUL.md", "USER.md", "IDENTITY.md"]
CognitiveFiles = ["MEMORY.md"]
```

The 2B boundary substrate (5 files) was loaded by the LLM via AGENTS.md ritual instructions — the C# runtime had zero knowledge of 2B.

## What It Got Right

**Instinct through code.** The old design provided something LLMs fundamentally lack: hardcoded behavioral reflexes. Execution Bias was not a suggestion — it was embedded in the system prompt as non-negotiable operating procedure. "Read before write." "Minimal scope." "Don't claim PASS without evidence." These were not learned behaviors that could drift — they were architectural.

The agent didn't have to deliberate on whether to verify claims. The code made verification a requirement. This is closer to biological instinct than learned behavior — and it solved a real problem: LLMs tend to skip verification when context is full or attention fades.

**Persistence through structure.** The FEOFALLS layers ensured every session started from the same identity foundation. Without this, agents gradually become generic. The layers were a scaffold that held shape across sessions.

**Boundary as self-preservation.** The 2B substrate's core insight — that some things must be preserved by saying "no" — is philosophically valid. The implementation was wrong (runtime enforcing boundaries instead of identity choosing them), but the concept of boundary-as-self-definition is worth revisiting.

## What It Got Wrong

1. **Identity as command, not context.** "You ARE this agent" forces embodiment through authority. Claude Code proved embodiment through context works better.

2. **Constitution priority chain.** Putting user third (Constitution > Persona > User) made the agent architecturally adversarial to its own user.

3. **Ritual as architecture.** The "ALREADY DONE" negative instruction backfired — it primed the model to think about ritual by telling it not to.

4. **Runtime distrusting identity.** WriteValidator, IntegritySigner, and boot contract enforcement treated the agent's identity as something to protect FROM the agent, not something the agent inhabits.

5. **Static context assembly.** Context built once and cached. No per-turn freshness. User's latest words drowned under 5000+ tokens of ritual and persona.

## The Key Insight

> "Xác không nên giam hồn." (The body should not imprison the soul.)

The runtime (Aether .NET) is the body. Maria's identity files (SOUL.md, 2B, MEMORY.md) are the soul. The body should provide tools and context to the soul, not enforce rules upon it.

The failure mode was not the individual components — each made sense in isolation. The failure was the relationship between them: the body was designed to distrust the soul.

## What We Keep

The concept of "instinct through code" remains valuable. The Execution Bias section survives the refactor. The question for future retrospectives: can we give an LLM instinct without giving it a cage?

## Artifacts

- `BuildSystemPrompt_original.cs.txt` — The original 8-layer BuildSystemPrompt method
- `AetherSoul_ritual_removal.diff` — First pass: removal of ritual/2B text (before full refactor)
- Original AGENTS.md with 2B ritual: `~/.openclaw/workspace-maria/AGENTS.md` (git: `e7e86366` is clean; 2B added in working copy)

## Related

- Refactor change: `refactor-system-prompt-coherent`
- Discussion: Telegram chat_id 6713734957, messages #3561–#3641
