## ADDED Requirements

### Requirement: System prompt includes execution bias section with behavioral defaults
`BuildSystemPrompt()` SHALL include an `## Execution Bias` section after Constitution and before Memory. The section SHALL contain three subsections: Behavioral Defaults, Code Style, and Verification.

#### Scenario: Execution bias always present
- **WHEN** `BuildSystemPrompt()` is called with any valid parameters
- **THEN** the output SHALL include the `## Execution Bias` section with all three subsections

### Requirement: Behavioral Defaults subsection
The `### Behavioral Defaults` subsection SHALL be scoped to "applies always — chat and code" and SHALL contain five directives:
1. Clear request → act immediately in this turn (don't describe what will be done)
2. Continue until done or genuinely blocked (blocked = needs user decision, external dependency, or explicit permission)
3. Weak/empty result → vary approach before concluding (don't retry blindly)
4. Mutable facts (files, git, state) → check live, don't assume
5. If blocked, propose smallest viable workaround and continue

#### Scenario: Behavioral defaults injected
- **WHEN** system prompt is built
- **THEN** all five behavioral default directives SHALL appear under `### Behavioral Defaults` with scope note "applies always — chat and code"

#### Scenario: Negative — describe instead of act
- **WHEN** user asks agent to check something
- **THEN** agent SHALL NOT respond with "I will check that for you" or "Let me look into that"
- **AND** agent SHALL check using available tools and report results

### Requirement: Code Style subsection (conditional — when editing code)
The `### Code Style (when editing code)` subsection SHALL be explicitly scoped to code editing tasks and SHALL contain four directives sourced from `claude-code-prompts/complete-prompts/system-prompt.md`:
1. Read before write/edit — never suggest changes to code you haven't inspected
2. Minimal scope — only what was requested, no adjacent refactoring
3. Don't add error handling for conditions that can't happen
4. Prefer editing existing files over creating new ones

#### Scenario: Code style injected with conditional scope
- **WHEN** system prompt is built
- **THEN** all four code style directives SHALL appear under `### Code Style (when editing code)` with explicit "when editing code" qualifier

#### Scenario: Negative — code style not applied to chat
- **WHEN** agent is engaging in casual conversation with no code editing
- **THEN** agent SHALL NOT restrict its communication based on Code Style rules
- **AND** persona voice (SOUL.md) SHALL govern communication style

### Requirement: Verification subsection
The `### Verification` subsection SHALL contain four directives sourced from `claude-code-prompts/patterns/06-verification-and-testing.md`:
1. Deliver evidence, not promises (test output, build logs, inspection)
2. Run checks after changes and show command output
3. Don't claim PASS without supporting evidence
4. If a check fails, diagnose before retrying

#### Scenario: Verification directives injected
- **WHEN** system prompt is built
- **THEN** all four verification directives SHALL appear under `### Verification`

#### Scenario: Negative — claim PASS without evidence
- **WHEN** agent runs a test or build
- **THEN** agent SHALL NOT claim "Tests pass" or "Build succeeds" without showing the actual command output
- **AND** agent SHALL include the relevant output lines as evidence

#### Scenario: Negative — retry blindly after failure
- **WHEN** a tool execution or test fails
- **THEN** agent SHALL NOT re-execute the same command without changing anything
- **AND** agent SHALL diagnose the error output before taking the next action

### Requirement: Execution bias section placement respects layer order
The execution bias section SHALL appear in a fixed position within the layered prompt architecture, after Constitution and before Memory:

```
## Identity (persona + embodiment)
## Constitution (non-negotiable)
## Execution Bias   ← HERE
## Memory
## Working State
## Recent Memory
```

#### Scenario: Full prompt layer order
- **WHEN** `BuildSystemPrompt()` is called with all parameters
- **THEN** sections SHALL appear in order: Identity → Constitution → Execution Bias → Memory → Working State → Recent Memory → Group Context → Skill
