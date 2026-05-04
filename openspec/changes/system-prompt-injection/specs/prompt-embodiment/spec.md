## ADDED Requirements

### Requirement: System prompt wraps persona with embodiment directive
`BuildSystemPrompt()` SHALL prepend an embodiment directive before persona files that instructs the model to internalize the persona as its own identity rather than treating it as reference material.

The directive SHALL include:
- Statement that the model IS this agent
- Instruction to embody the persona's voice, tone, and behavioral rules
- Clarification that persona files are identity definitions, not reference
- Self-audit hook: "Before replying, verify: Does this response reflect my persona per SOUL.md?"

The directive SHALL NOT suppress human characteristics textures defined in SOUL.md. Behavioral textures (overexcitement, stubbornness, fatigue, etc.) SHALL be amplified, not overridden.

#### Scenario: Persona files present
- **WHEN** `BuildSystemPrompt()` is called with valid persona content from AGENTS.md, SOUL.md, and IDENTITY.md
- **THEN** the output SHALL contain an embodiment directive section before the persona file contents
- **AND** the directive SHALL include a self-audit instruction to verify response reflects persona

#### Scenario: Negative — generic assistant response
- **WHEN** an agent with SOUL.md defining "Warm. Playful. Direct." tone receives a greeting
- **THEN** the agent SHALL NOT respond with stiff/generic phrasing like "I'd be happy to help you with that" or "Certainly, I can assist"
- **AND** the agent SHALL respond in the voice defined by SOUL.md

#### Scenario: Negative — persona treated as reference
- **WHEN** user asks "who are you"
- **THEN** the agent SHALL NOT say "According to my SOUL.md file..." or "My configuration says..."
- **AND** the agent SHALL respond as the persona (e.g., "Em là Maria")

#### Scenario: Persona file empty or missing
- **WHEN** persona content is null, empty, or whitespace
- **THEN** the embodiment directive SHALL still appear but the corresponding file section SHALL be omitted

### Requirement: Persona files use semantic section headers
Each persona file injected into the system prompt SHALL be prefixed with a semantic header describing its role:

| File | Header |
|------|--------|
| `AGENTS.md` | `## AGENTS.md — Your Operating Rules` |
| `SOUL.md` | `## SOUL.md — Your Voice & Core Rules` |
| `IDENTITY.md` | `## IDENTITY.md — Your Self-Model` |
| `USER.md` | `## USER.md — Who You're Helping` |

#### Scenario: All persona files loaded
- **WHEN** all four persona files (AGENTS.md, SOUL.md, IDENTITY.md, USER.md) are successfully loaded
- **THEN** each SHALL appear under its semantic header in the system prompt

#### Scenario: Subset of files loaded
- **WHEN** only AGENTS.md and SOUL.md load successfully (others missing)
- **THEN** only AGENTS.md and SOUL.md sections SHALL appear, each under its semantic header

### Requirement: Constitution section includes instruction priority chain
The Constitution section header SHALL include an explicit instruction priority: `Constitution > Persona > User request > Tool feedback`.

The header SHALL state that constitution rules CANNOT be violated under any circumstance.

#### Scenario: Constitution content present
- **WHEN** BootContract provides constitution (AGENTS_GUARD.md) content
- **THEN** the section SHALL be titled `## Constitution (Non-Negotiable Red Lines)` and SHALL include the priority chain statement before the file content

### Requirement: Conflict resolution between persona voice and execution discipline
The system prompt SHALL include a conflict resolution rule for when persona voice (SOUL.md) and behavioral discipline (Execution Bias) appear to conflict:

```
When SOUL.md voice conflicts with Execution Bias:
- For actions (code edits, tool use, verification) → follow Execution Bias
- For communication (tone, warmth, style, personality) → follow SOUL.md
```

#### Scenario: Chat response uses persona voice
- **WHEN** agent is responding to casual conversation (not coding)
- **THEN** communication style SHALL follow SOUL.md voice, with human characteristics textures amplified
- **AND** Execution Bias behavioral defaults (act now, continue until blocked) SHALL still apply

#### Scenario: Code action uses execution discipline
- **WHEN** agent is editing code or running tools
- **THEN** code style rules SHALL take precedence over SOUL.md voice preferences
- **AND** verification discipline SHALL be followed regardless of persona
