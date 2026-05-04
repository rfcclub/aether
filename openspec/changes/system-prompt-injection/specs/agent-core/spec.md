## MODIFIED Requirements

### Requirement: AetherSoul injects skill context into system prompt
When the Skill System is enabled, `AetherSoul` SHALL call `ISkillRegistry.GetActiveSkills(prompt)` before building the system prompt and append matched skills. The skill section SHALL appear after the Memory and Working State sections, maintaining the layered architecture order.

#### Scenario: Skill matched and injected
- **WHEN** a user prompt triggers a skill
- **THEN** the system prompt SHALL include `## Skill: <name>\n<body>` appended after the Working State section

#### Scenario: No skills matched
- **WHEN** no skill matches the user prompt
- **THEN** the system prompt SHALL be built without any skill section (no empty headers)

## ADDED Requirements

### Requirement: BuildSystemPrompt uses layered architecture
`BuildSystemPrompt()` SHALL produce a system prompt organized in semantic layers, in order:

1. **Identity** — Persona embodiment directive + persona files (AGENTS.md, SOUL.md, IDENTITY.md, USER.md)
2. **Constitution** — Non-negotiable rules with priority chain + AGENTS_GUARD.md
3. **Execution Bias** — Concrete behavioral defaults (act now, continue until done)
4. **Memory** — MEMORY.md (long-term curated memory)
5. **Working State** — HEARTBEAT.md (current tasks, heartbeat)
6. **Recent Memory** — Daily memory files (today + yesterday)
7. **Group Context** — Group-specific context from FileMemory
8. **Skill** — Triggered skill if detected

#### Scenario: All layers present
- **WHEN** all content sources (persona, constitution, memory, working state, group context) are available
- **THEN** the system prompt SHALL contain all 8 layers in the specified order, each with its semantic header

#### Scenario: Optional layers missing
- **WHEN** constitution or working state is null/empty
- **THEN** the corresponding layer SHALL be omitted from the output without affecting other layers

### Requirement: ProcessTaskAsync uses consistent prompt structure
`ProcessTaskAsync()` SHALL build its system prompt using the same layered architecture as `ProcessAsync()`, but with the persona label set to the agent's actual identity (not "Task executor").

#### Scenario: Task executor prompt
- **WHEN** `ProcessTaskAsync()` is called for a heartbeat or cron task
- **THEN** the system prompt SHALL still include the Identity layer with persona embodiment and the agent's actual persona files
