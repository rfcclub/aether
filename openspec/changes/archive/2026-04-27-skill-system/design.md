## Context

Aether already has tool execution (bash, read, write, edit) and memory system. Skill system adds a declarative layer — markdown files that define reusable procedures the agent can invoke. This enables non-code extensibility: add a skill by dropping a .md file, no recompile needed.

Current state: skill system code just implemented (2026-04-26), not yet committed. Four new classes in `src/Aether/Skills/`: SkillInterfaces, SkillParser, SkillRegistry, SkillTrigger, SkillEvolution.

## Goals / Non-Goals

**Goals:**
- Load skills from `skills/` directory at startup
- Explicit trigger via `/<skill-name>` — highest priority
- Auto-trigger via keyword overlap when similarity >= 0.35
- Inject skill body into AetherSoul system prompt when activated
- Track skill effectiveness, flag recidivism for self-improvement pipeline

**Non-Goals:**
- Semantic/embedding-based similarity (keyword overlap only for v1)
- Hot-reload of skills at runtime
- Skill versioning or dependencies between skills
- Skill chaining (one skill triggering another)

## Decisions

**1. Keyword overlap over embeddings for auto-trigger**

Chose simple word-overlap scoring (message words vs description + when_to_use words) over embedding-based similarity.

Rationale: No external ML dependency, fast, predictable. Embeddings would require a separate model call or vector DB.

Alternatives considered: embeddings (too heavy), BM25 (overkill for skill数量).

**2. Separate ISkillTrigger from ISkillRegistry**

SkillTrigger handles detection logic. SkillRegistry handles storage and resolution. Decoupled — allows testing trigger logic independently.

**3. Skill body injected per-request, not persisted**

Skills apply to a single turn only. No session-level skill state. Simpler, avoids contamination across unrelated conversations.

**4. Recidivism as PromotionCandidate**

When a skill fails 3+ times in last 10 uses (avg delta < 0), it generates a PromotionCandidate with source="recidivism". This bridges skill evolution to the existing memory promotion pipeline in IMemorySystem.TryPromoteAsync.

## Risks / Trade-offs

- **Keyword threshold 0.35 may trigger too often or not enough** → adjustable, logs trigger reason for tuning
- **No skill versioning** → a skill update replaces old version with no migration path
- **Malformed SKILL.md silently skipped** → logs warning, doesn't crash startup
- **Skill evolution in-memory only (no persistence across restarts)** → usage records reset on process restart; acceptable for v1 since recidivism detection window is recent

## Open Questions

- Should skills support dependencies (skill A triggers skill B)?
- Should auto-apply skills (auto_apply: true) require confirmation or run silently?
- Where should skills directory live — alongside binary or in group folders?