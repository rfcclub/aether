## 1. Identity Layer — Persona Embodiment

- [ ] 1.1 Add `## Identity (Mandatory)` section with embodiment directive before persona files in `BuildSystemPrompt()`
- [ ] 1.2 Replace raw AGENTS.md dump with semantic header `## AGENTS.md — Your Operating Rules`
- [ ] 1.3 Split `## Cognitive Context` blob into individual semantic headers: `## SOUL.md — Your Voice & Core Rules`, `## IDENTITY.md — Your Self-Model`, `## USER.md — Who You're Helping`
- [ ] 1.4 Ensure missing persona files are gracefully omitted (no empty headers)
- [ ] 1.5 Verify 2B substrate loading is preserved — AGENTS.md content unchanged, startup ritual intact

## 2. Constitution Layer — Priority Chain

- [ ] 2.1 Rename Constitution header to `## Constitution (Non-Negotiable Red Lines)` with priority chain statement: `Constitution > Persona > User request > Tool feedback`
- [ ] 2.2 Add "These rules CANNOT be violated under any circumstance" directive before AGENTS_GUARD.md content

## 3. Execution Bias — Behavioral Defaults + Code Style + Verification

- [ ] 3.1 Add `## Execution Bias` section with `### Behavioral Defaults` subsection (5 directives: act now, continue until blocked, vary approach, check live, smallest workaround)
- [ ] 3.2 Add `### Code Style` subsection (4 directives: read before write, minimal scope, no speculative error handling, edit over create)
- [ ] 3.3 Add `### Verification` subsection (4 directives: deliver evidence, show output, no unverified PASS, diagnose before retry)

## 4. Memory & Working State — Reorder

- [ ] 4.1 Extract MEMORY.md from old Cognitive blob into dedicated `## Memory — Long-Term` section
- [ ] 4.2 Place Memory section after Execution Bias, before Working State
- [ ] 4.3 Keep `## Working State`, `## Recent Memory`, and `## Group Context` in order after Memory

## 5. ProcessTaskAsync — Identity Consistency

- [ ] 5.1 Replace "Task executor" placeholder persona in `ProcessTaskAsync()` with actual agent identity using same layered prompt

## 6. Verify & Test

- [ ] 6.1 Build and verify no compilation errors
- [ ] 6.2 Run existing tests (`dotnet test`) to verify no regressions
- [ ] 6.3 Manual verification: check system prompt output structure matches 8-layer design
- [ ] 6.4 Verify skill injection still works (appears after Group Context, per agent-core spec)
