## ADDED Requirements

### Requirement: Skill discovery tools

Aether SHALL provide tools for listing and reading skills from the active agent workspace.

#### Scenario: List skills
- **WHEN** the model calls `skill_list`
- **THEN** the tool SHALL return available skill names from workspace skill directories containing `SKILL.md`

#### Scenario: Read a skill
- **WHEN** the model calls `skill_read` with a valid skill name
- **THEN** the tool SHALL return that skill's `SKILL.md` content

#### Scenario: Reject path traversal
- **WHEN** the model calls `skill_read` with a name containing path traversal
- **THEN** the tool SHALL reject the request without reading outside the skills directory

