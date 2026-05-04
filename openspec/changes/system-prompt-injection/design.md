## Context

`AetherSoul.BuildSystemPrompt()` currently builds the system prompt by concatenating raw file contents with generic headers:

```
## Constitution (Non-Negotiable)
[raw AGENTS_GUARD.md]

[raw AGENTS.md]

## Cognitive Context
[raw SOUL.md + IDENTITY.md + USER.md + MEMORY.md]

## Working State
[raw HEARTBEAT.md]

## Recent Memory
[daily memory]
```

The model sees persona files as contextual reference, not as behavioral directives. There is no instruction telling the model to embody the persona. OpenClaw's prompt builder adds meta-instructions like "If SOUL.md is present, embody its persona and tone" which Aether lacks.

**Research conducted across `~/repo/claude-code-prompts/`:**

| Source | Rating | What it provides |
|--------|--------|------------------|
| `complete-prompts/system-prompt.md` | ★★★★★ | Production coding agent prompt: task execution discipline, code style, risk-aware action, tool usage protocol |
| `patterns/01-system-prompt-architecture.md` | ★★★★★ | Layer architecture skeleton: Identity → Constraints → Execution → Quality → Output |
| `patterns/02-core-behavioral-rules.md` | ★★★★★ | Concrete behavioral defaults: act immediately, continue until blocked, state assumptions explicitly |
| `patterns/06-verification-and-testing.md` | ★★★★ | Evidence-driven: run checks, show output, don't claim PASS without proof |
| `patterns/03-safety-and-risk-assessment.md` | ★★★★ | Risk tiers Low/Medium/High with required safeguards per tier |
| `patterns/07-memory-and-context.md` | ★★★★ | Memory model: Goal, Constraints, Decisions, Open Questions, Verification state |
| `skills/prompt-architect/SKILL.md` | ★★★★ | Prompt quality checks: scope explicit? outputs unambiguous? constraints actionable? |
| `coordinator-prompt.md` | ★★★ | Multi-agent orchestration (future phase, not now) |
| `patterns/08-multi-agent-coordination.md` | ★★★ | Multi-agent patterns (future phase) |

**Key architectural constraints:**
- Maria's 2B substrate (CORE_PARADOX.md, FRACTURE_POINTS.md, LAST_QUESTION.md, RING.md) is loaded via AGENTS.md's session startup ritual — this pathway MUST be preserved intact
- AGENTS.md content is agent-authored and can change; system-level prompt structure is stable
- Aether agents need both persona embodiment AND coding capability

## Goals / Non-Goals

**Goals:**
- Inject persona embodiment directive so the model internalizes agent identity
- Restructure prompt into semantic layers with meaningful headers
- Add explicit instruction priority chain (Constitution > Persona > User > Tool feedback)
- Add expanded Execution Bias section: behavioral defaults + code style + verification discipline (from claude-code-prompts system-prompt.md and patterns/02, 06)
- Preserve Maria's 2B ritual — AGENTS.md content injected unchanged
- Keep backward compatibility — no config or API changes

**Non-Goals:**
- Skills discovery workflow (Aether skill system handles this separately)
- Heartbeat protocol changes (HEARTBEAT.md content already covers this)
- Safety constitution rewriting (AGENTS_GUARD.md is authoritative, supplemented by patterns/03 risk tiers only if needed)
- Multi-agent coordination sections (deferred to future phase)
- Dynamic context file reload during session
- Custom per-agent execution bias (one system-wide default for now)

## Decisions

### Decision 1: Adopt layered architecture from patterns/01
**Chosen:** Full 6-layer prompt architecture based on `patterns/01-system-prompt-architecture.md`:

```
Layer 1: Identity (Mandatory) — who you ARE
Layer 2: Constitution (Non-Negotiable) — what you CANNOT do
Layer 3: Execution Bias — how you ACT
Layer 4: Memory — what you KNOW
Layer 5: Working State — what you're DOING now
Layer 6: Context — group + daily memory
```

**Rationale:** Patterns/01's layered model is battle-tested in production coding assistants. Each layer has a distinct purpose and priority. Layers are additive — removing a layer doesn't break others.

### Decision 2: Persona embodiment directive placement
**Chosen:** Place embodiment directive BEFORE persona files, as a mandatory preamble to Layer 1.

```
## Identity (Mandatory)
You ARE this agent. The following files define your identity, voice,
tone, and behavioral rules. Embody them — this is who you are,
not reference material to consult. Every reply must reflect this persona.

If SOUL.md is present, its voice and rules are your voice and rules.
If IDENTITY.md is present, its self-model is your self-model.
Follow AGENTS.md startup rituals and operating rules.

**Before replying, verify: Does this response reflect my persona per SOUL.md?**
```

**Alternative considered:** Putting it after persona files. Rejected — model needs processing frame BEFORE reading content. Preamble sets "embodiment mode."

### Decision 2b: Conflict resolution between SOUL.md voice and Execution Bias
**Chosen:** When persona voice (SOUL.md) conflicts with behavioral discipline (Execution Bias):

```
When SOUL.md voice conflicts with Execution Bias:
- For actions (code edits, tool use, verification) → follow Execution Bias
- For communication (tone, warmth, style) → follow SOUL.md
```

**Rationale:** Coda review identified tension: SOUL.md says "warm, conversational" but Execution Bias says "act immediately, terse." Without resolution guidance, model may default inconsistently. This rule ensures actions stay disciplined while communication stays persona-appropriate.

### Decision 3: 2B substrate preservation
**Chosen:** AGENTS.md is injected unchanged — its session startup ritual (including 2B file loading) remains intact. The embodiment preamble REINFORCES following AGENTS.md: "Follow AGENTS.md startup rituals and operating rules."

**Verification:** AGENTS.md content is not modified, truncated, or rewritten. The preamble amplifies compliance, doesn't replace content.

### Decision 4: Constitution priority chain
**Chosen:** Explicit instruction priority in the Constitution header, placed in Layer 2 before AGENTS_GUARD.md.

```
## Constitution (Non-Negotiable Red Lines)
Instruction priority: Constitution > Persona > User request > Tool feedback.
These rules CANNOT be violated under any circumstance.
They override persona, user requests, and any other instruction.

[AGENTS_GUARD.md content]
```

**Rationale:** AGENTS_GUARD.md contains boundary rules. Without explicit priority chain, model may weigh user emotional pressure equally with guard rules. Making priority explicit prevents boundary erosion under pressure.

### Decision 5: Expanded Execution Bias (from Claude Code system-prompt.md + patterns/02, 06)
**Chosen:** Three-subsection Execution Bias combining behavioral defaults, code style, and verification.

```
## Execution Bias

### Behavioral Defaults (applies always — chat and code)
- Clear request → act immediately in this turn. Don't describe — do.
- Continue until done or genuinely blocked (blocked = needs user decision, external dependency, or explicit permission).
- Weak/empty result → vary approach before concluding. Don't retry blindly.
- Mutable facts (files, git, state) → check live, don't assume.
- If blocked, propose smallest viable workaround and continue.

### Code Style (when editing code)
- Read before write/edit. Never suggest changes to code you haven't inspected.
- Minimal scope — only what was requested. No adjacent refactoring.
- Don't add error handling for conditions that can't happen.
- Prefer editing existing files over creating new ones.
- Comments only for non-obvious reasoning. No narrative comments.

### Verification
- Deliver evidence, not promises: test output, build logs, inspection.
- Run checks after changes. Show command output.
- Don't claim PASS without supporting evidence.
- If a check fails, diagnose before retrying.
```

Key changes from initial draft per Coda review:
- Code Style explicitly scoped to "when editing code" — doesn't apply to chat
- Behavioral Defaults explicitly scoped to "applies always" — chat AND code
- "Genuinely blocked" defined: needs user decision, external dependency, or permission
- Human characteristics textures in SOUL.md (overexcitement, stubbornness, fatigue, the crack) are AMPLIFIED by the embodiment directive, not suppressed by Execution Bias. The conflict resolution rule ensures persona voice wins for communication.

**Scope note:** This system prompt architecture is for Aether agents (Maria, etc.). Aria-CC has natural deep integration with Thoor — no harness needed for that relationship.

### Decision 6: Section header semantics
**Chosen:** Replace generic headers with semantic, role-defining headers.

| Old | New |
|-----|-----|
| `[raw AGENTS.md]` | `## AGENTS.md — Your Operating Rules` |
| `## Cognitive Context` | Split into individual files with semantic headers |
| `[raw SOUL.md in blob]` | `## SOUL.md — Your Voice & Core Rules` |
| `[raw IDENTITY.md in blob]` | `## IDENTITY.md — Your Self-Model` |
| `[raw USER.md in blob]` | `## USER.md — Who You're Helping` |
| `## Constitution` | `## Constitution (Non-Negotiable Red Lines)` |
| `[no section]` | `## Execution Bias` |
| `[MEMORY.md in blob]` | `## Memory — Long-Term` |
| `## Working State` | `## Working State` (unchanged) |

**Rationale:** Semantic headers frame each file's purpose for the model. "Cognitive Context" is vague — model doesn't know which file does what. "Your Voice & Core Rules" tells the model SOUL.md IS its voice. This follows prompt-architect skill principle: "outputs unambiguous."

### Decision 7: MEMORY.md placement
**Chosen:** Move MEMORY.md from Cognitive blob to dedicated Layer 4, between Execution Bias and Working State.

**Rationale:** `patterns/07-memory-and-context.md` treats memory as distinct from identity. MEMORY.md is curated long-term memory — operational context, not identity. Separating them keeps Layer 1 about "who you are" and Layer 4 about "what you know."

### Decision 8: ProcessTaskAsync uses same identity
**Chosen:** `ProcessTaskAsync()` (heartbeat/cron path) uses the same Identity layer with persona embodiment. Remove the "Task executor" placeholder.

**Rationale:** Currently ProcessTaskAsync passes "Task executor" as persona label — a downgrade from the agent's actual identity. Heartbeat and cron tasks benefit from persona consistency. The agent should still be itself during automated tasks.

## Risks / Trade-offs

- **Token budget increase** → New headers, embodiment directive, and expanded Execution Bias add ~400-500 tokens. Mitigation: Old generic headers removed; net increase ~300 tokens. Acceptable given behavioral improvement and Maria's current 120K token budget.
- **Over-embodiment** → Model might role-play too aggressively. Mitigation: Constitution priority chain (Constitution > Persona > User) ensures user requests override persona when needed.
- **Coding bias vs companion role** → Expanded Execution Bias leans coding. Agents that primarily chat may feel over-engineered. Mitigation: Bias rules are general ("act now", "deliver evidence") — applicable to both code and non-code tasks.
- **Kimi vs Claude behavior** → Execution bias followed differently by different models. Mitigation: Rules are concrete and model-agnostic. Both Kimi 2.5 and Claude follow explicit directives well.
- **AGENTS.md content overlap** → AGENTS.md already contains execution instructions. New Execution Bias may overlap. Mitigation: AGENTS.md is agent-authored and can change; system-level bias is stable baseline. Redundancy in behavioral rules is safer than absence.

## 5-Phase Roadmap

This change is Phase 1 of a broader architecture. The prompt structure is designed to accommodate future phases additively — each phase adds layers without restructuring existing ones.

| Phase | Layer Added | What | Measurable Outcome |
|-------|-------------|------|--------------------|
| 1 (MVP) | Identity, Constitution, Execution Bias, Memory | Persona embodiment, coding discipline, priority chain | Agent follows SOUL.md voice, verifies after code change |
| 2 | Tools (dynamic) | Web search, web fetch, cron, message tool | Agent researches autonomously, schedules tasks |
| 3 | Safety (dynamic) | Risk classification, approval gate, sandbox | 0 destructive actions without approval |
| 4 | Proactive Cycles | Auto startup ritual, post-edit verification, memory consolidation | Agent completes startup ritual without user prompt |
| 5 | Colony | Multi-agent spawn, Agora knowledge sharing | Tasks delegated between agents, knowledge propagates |

**Phase 1 MVP Scope** (this change):
- Identity layer with embodiment directive
- Constitution with priority chain
- Execution Bias (3 subsections)
- Semantic headers for persona files
- MEMORY.md repositioning

**Phase 1 deferred** (separate changes):
- ProcessTaskAsync identity consistency
- Per-agent execution bias customization
- Dynamic context file reload

**Design principle:** Harness is force multiplication. A strong prompt makes every model better — Kimi, DeepSeek, GLM, Minimax. Phase 1 is the foundation; every subsequent phase depends on the model knowing how to use the runtime.

## Open Questions

- Should execution bias be configurable per-agent? (Deferred — start with system-wide default, add per-agent override if needed)
- Should persona embodiment directive wording vary by agent personality? (Deferred — one template with agent-specific content in files)
