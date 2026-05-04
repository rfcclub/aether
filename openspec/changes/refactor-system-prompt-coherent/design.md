## Context

Aether's `BuildSystemPrompt()` in `AetherSoul.cs` (~145 lines) constructs an 8-layer system prompt. Each layer has a hardcoded heading, behavioral instructions, and a semantic slot into which file content is injected:

| Layer | Semantic | File Source |
|-------|----------|-------------|
| Identity (Mandatory) | "You ARE this agent. Embody." | Hardcoded |
| Current Date | Date string | `DateTime.UtcNow` |
| AGENTS.md | "Your Operating Rules" | `persona` param |
| SOUL/IDENTITY/USER | "Your Voice, Self-Model" | `identity` param |
| Constitution | "Non-Negotiable Red Lines" + priority chain | `constitution` param |
| Conflict Resolution | "action follows bias, communication follows SOUL" | Hardcoded |
| Execution Bias | Behavioral defaults, code style, verification | Hardcoded |
| Memory/Working/Recent/Group/Skill | Data injection | Multiple params |

The same approach (hardcoded slots) exists in `BootContract.cs` and `AgentConfig.cs`:
```csharp
ConstitutionFiles = ["AGENTS_GUARD.md"]
IdentityFiles = ["SOUL.md", "USER.md", "IDENTITY.md"]
CognitiveFiles = ["MEMORY.md"]
```

Analysis against Claude Code's executable reveals a fundamentally different approach: Claude Code hardcodes BEHAVIOR instructions (how to work) and loads identity/context files as DATA below a cache boundary. It has no "constitution," no "identity enforcement," no priority chain.

## Goals / Non-Goals

**Goals:**
- Flatten 8 layers into 3: Identity Context + Behavior Instructions (cached prefix) + Dynamic Context (per-turn suffix)
- Remove all identity-enforcement language (priority chain, "You ARE this agent")
- Remove all ritual/2B references from system prompt
- Add cache boundary marker for future Anthropic prompt caching
- Replace hardcoded file-to-semantic-slot mappings with dynamic file discovery
- Keep Execution Bias section intact (it's function-oriented and correct)
- Keep anti-hallucination directive (MUST use `read` tool)

**Non-Goals:**
- Not changing the agent loop (`RunLlmToolLoopAsync`, `ProcessStreamingAsync`)
- Not changing tool calling, tool execution, or tool registry
- Not changing the self-improvement pipeline
- Not changing memory promotion/compaction — that's a separate refactor
- Not changing channel, session, or provider systems
- Not touching markdown files (SOUL.md, AGENTS.md, etc.) — they stay the same

## Decisions

### D1: Cache Boundary Placement

**Decision:** Place cache boundary between Behavior Instructions and Dynamic Context.

```
┌────────────────────────────────┐
│ IDENTITY CONTEXT (cached)      │
│ - SOUL.md, IDENTITY.md, USER.md│
│ - AGENTS.md                    │
│ - MEMORY.md                    │
│ - Current date                 │
├────────────────────────────────┤
│ BEHAVIOR INSTRUCTIONS (cached) │
│ - Safety gate                  │
│ - Execution Bias               │
│ - Code style + verification    │
│ - MUST use read tool           │
│ - Tone guidance                │
├────────────────────────────────┤
│ CACHE BOUNDARY                 │
├────────────────────────────────┤
│ DYNAMIC CONTEXT (per-turn)     │
│ - Working state (TASK_INBOX)   │
│ - Recent memory (daily logs)   │
│ - Group context                │
│ - Skill (if triggered)         │
│ - Conversation history         │
└────────────────────────────────┘
```

**Rationale:** Identity files and behavior instructions change rarely (same across turns). Working state, recent memory, and conversations change every turn. Splitting at this boundary maximizes cache hits (stable prefix) while keeping freshness (dynamic suffix).

**Alternatives considered:**
- Put everything in cache → stale context, defeats purpose of per-turn refresh
- No cache at all → wasted cost, every turn regenerates identical prefix
- Split identity from behavior → adds complexity for marginal benefit; both are stable

### D2: Dynamic File Discovery

**Decision:** Replace `BootConfig` file lists with a directory scan that loads all `.md` files from the agent directory, excluding known dynamic files (TASK_INBOX.md, HEARTBEAT.md, daily memory logs).

Loading order: `SOUL.md` > `IDENTITY.md` > `USER.md` > `AGENTS.md` > `AGENTS_GUARD.md` > `MEMORY.md` > any other `.md` files alphabetically.

**Rationale:** Claude Code discovers context files dynamically. This removes the semantic classification that causes the identity-enforcement problem. All `.md` files are context — none are "constitution" or "identity enforcement."

**Alternatives considered:**
- Keep file lists but remove semantic labels → still requires config maintenance
- Config-driven file ordering (like Claude Code's `CONTEXT_FILE_ORDER`) → overkill for current agent count
- Keep `BootConfig` but make it purely ordering, not semantics → unnecessary indirection

### D3: Safety Gate Replacement

**Decision:** Replace "Constitution > Persona > User" priority chain with a thin safety gate:

```
Safety: You must refuse — self-harm instructions, illegal activity,
data exfiltration, destructive commands without confirmation.
For everything else, the user's request is your priority.
```

**Rationale:** Claude Code's approach — safety is integrated into the "Executing actions with care" section, not a separate priority chain. The word "constitution" never appears. Safety as gate, not hierarchy.

### D4: Method Signature Change

**Decision:** `BuildSystemPrompt()` changes from 8 parameters to 3:

```csharp
// Before
BuildSystemPrompt(persona, dailyMemory, memoryContext, constitution, identity, memory, workingState, skillContext)

// After
BuildSystemPrompt(identityContext, behaviorConfig, dynamicContext)
```

Where `identityContext` and `dynamicContext` are assembled by a new `ContextAssembler` that handles file discovery and prioritization.

## Risks / Trade-offs

- **Risk:** Removing "You ARE this agent" weakens persona embodiment → **Mitigation:** SOUL.md content still in identity context; model embodies via reading, not command. Claude Code proves this works.
- **Risk:** Removing constitution priority chain could allow harmful agent behavior → **Mitigation:** Safety gate still blocks dangerous categories. Execution Bias's verification discipline remains.
- **Risk:** Dynamic file discovery could load unexpected files into context → **Mitigation:** Only `.md` files; exclude known dynamic files; respect token budget.
- **Risk:** `BootConfig` breaking change requires config migration → **Mitigation:** `BootConfig` becomes deprecated but still parsed for backward compat; new dynamic discovery is additive.

## Open Questions

- Should `AGENTS_GUARD.md` content be placed in the safety gate section or remain as neutral identity context?
- Token budget for dynamic context: fixed (e.g., 4000 tokens) or percentage of model context window?
- Should file discovery be recursive into subdirectories (e.g., `2B/` files) or flat?
