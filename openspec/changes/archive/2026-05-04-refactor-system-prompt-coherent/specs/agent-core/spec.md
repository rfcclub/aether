# agent-core Specification Delta

## ADDED Requirements

### Requirement: Dynamic context file discovery

`AgentProfile` SHALL discover all `.md` files in the agent directory for context loading, without relying on hardcoded `ConstitutionFiles`, `IdentityFiles`, or `CognitiveFiles` lists.

#### Scenario: Files discovered from agent directory

- **WHEN** `AgentProfile.LoadIdentityContextAsync()` is called
- **THEN** all `.md` files in the agent directory SHALL be discovered
- **THEN** files SHALL be loaded in the priority order defined by `dynamic-context-loading` spec
- **THEN** no file SHALL receive a semantic label (e.g., "Constitution", "Non-Negotiable Red Lines")

### Requirement: Backward compatibility with BootConfig

`BootConfig` file lists (`ConstitutionFiles`, `IdentityFiles`, `CognitiveFiles`) SHALL be marked as deprecated. When present, they SHALL be treated as additional files to include rather than as semantic categories.

#### Scenario: Deprecated BootConfig still loads files

- **WHEN** `BootConfig.ConstitutionFiles` contains `["CUSTOM_RULES.md"]`
- **THEN** `CUSTOM_RULES.md` SHALL be included in the identity context
- **THEN** it SHALL NOT receive a "Constitution" heading
- **THEN** a deprecation warning SHALL be logged

### Requirement: Context assembler service

A new `ContextAssembler` service SHALL handle assembly of identity context and dynamic context from file discovery, daily memory, working state, and group context.

#### Scenario: Identity context assembled

- **WHEN** `ContextAssembler.AssembleIdentityContextAsync(agentDir)` is called
- **THEN** it SHALL return a string containing all discovered `.md` files in priority order
- **THEN** it SHALL prepend a minimal heading: "## Context Files"

#### Scenario: Dynamic context assembled

- **WHEN** `ContextAssembler.AssembleDynamicContextAsync(agentDir, sessionId)` is called
- **THEN** it SHALL return a string containing working state, recent memory, and group context
- **THEN** it SHALL respect the configured token budget

## MODIFIED Requirements

### Requirement: AetherSoul injects skill context into system prompt

When the Skill System is enabled, `AetherSoul` SHALL call `ISkillRegistry.GetActiveSkills(prompt)` before building the system prompt and append matched skills to the Dynamic Context section (below the cache boundary).

#### Scenario: Skill matched and injected

- **WHEN** a user prompt triggers a skill
- **THEN** the Dynamic Context section SHALL include `## Skill: <name>\n<body>`
- **THEN** the skill content SHALL appear BELOW the cache boundary marker

#### Scenario: No skills matched

- **WHEN** no skill matches the user prompt
- **THEN** the Dynamic Context section SHALL be built without any skill content

## REMOVED Requirements

None removed from base spec.
