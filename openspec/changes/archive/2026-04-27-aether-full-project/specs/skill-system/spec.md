## ADDED Requirements

### Requirement: Skill loader parses SKILL.md files
The skill system SHALL load all `*.md` files from a configurable `skills/` directory at startup, parsing YAML frontmatter and markdown body.

#### Scenario: Valid skill file loaded
- **WHEN** a `SKILL.md` file with valid frontmatter (`name`, `description`, `tools`) exists in the skills directory
- **THEN** the skill system SHALL register the skill and make it available for trigger detection

#### Scenario: Malformed frontmatter
- **WHEN** a skill file has invalid YAML frontmatter
- **THEN** the skill system SHALL log a warning with the file path and skip that skill (not crash)

#### Scenario: Empty skills directory
- **WHEN** the skills directory is absent or empty
- **THEN** the skill system SHALL start without error and log an informational message

### Requirement: Explicit skill trigger via slash command
The agent SHALL activate a skill when the user message starts with `/<skill-name>`.

#### Scenario: Exact name match
- **WHEN** user sends `/github-code-review review this PR`
- **THEN** the agent SHALL inject the `github-code-review` skill body into the system prompt for that turn

#### Scenario: Unknown slash command
- **WHEN** user sends `/nonexistent-skill foo`
- **THEN** the agent SHALL respond with a list of available skill names and not error

### Requirement: Auto-trigger by description similarity
The agent SHALL auto-activate a skill when the user message semantically matches a skill's `when_to_use` field above a threshold of 0.65.

#### Scenario: High-similarity match
- **WHEN** user sends "can you review this pull request for issues" and a skill has `when_to_use: "When user asks to review code or check a PR"`
- **THEN** the agent SHALL inject that skill's body into the system prompt

#### Scenario: No match above threshold
- **WHEN** user message has no skill similarity above 0.65
- **THEN** the agent SHALL proceed without skill injection

### Requirement: Skill body injected into system prompt
When a skill is activated, its markdown body SHALL be appended to the system prompt under a `## Skill: <name>` section.

#### Scenario: Skill injected for one turn
- **WHEN** a skill is activated
- **THEN** the skill content SHALL appear in the system prompt for that request only (not persisted across turns)

#### Scenario: Multiple skills active
- **WHEN** multiple skills match
- **THEN** the agent SHALL inject all matching skills in registration order
