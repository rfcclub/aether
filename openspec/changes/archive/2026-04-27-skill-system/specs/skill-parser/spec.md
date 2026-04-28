## ADDED Requirements

### Requirement: SKILL.md parser extracts frontmatter and body
The skill parser SHALL parse YAML frontmatter (between `---` markers) from the markdown body and return a SkillDefinition with all extracted fields.

#### Scenario: Valid frontmatter extracted
- **WHEN** a SKILL.md file contains:
  ```
  ---
  name: github-code-review
  description: Review pull requests
  when_to_use: When user asks to review code
  tools: [read, grep]
  auto_apply: false
  ---
  # Body content here
  ```
- **THEN** ParseSkillFile SHALL return SkillDefinition with Name="github-code-review", Description="Review pull requests", Body="# Body content here"

#### Scenario: Description required
- **WHEN** a SKILL.md file has no description in frontmatter
- **THEN** ParseSkillFile SHALL return null and log a warning

#### Scenario: Missing frontmatter defaults name to filename
- **WHEN** a SKILL.md file has no frontmatter
- **THEN** ParseSkillFile SHALL return null and log a warning about missing frontmatter

#### Scenario: Tools field parsed as array
- **WHEN** frontmatter has `tools: [read, grep, bash]`
- **THEN** the SkillDefinition.Tools array SHALL contain exactly three entries: "read", "grep", "bash"

#### Scenario: Auto_apply parsed as boolean
- **WHEN** frontmatter has `auto_apply: true`
- **THEN** SkillDefinition.AutoApply SHALL be true

### Requirement: Skill loader loads all .md files from directory
The skill loader SHALL recursively scan a directory for `*.md` files, parse each one, and return a list of valid SkillDefinition objects.

#### Scenario: Empty directory returns empty list
- **WHEN** LoadFromDirectoryAsync is called on a non-existent or empty directory
- **THEN** it SHALL return an empty list without throwing

#### Scenario: Malformed file skipped with warning
- **WHEN** one file in the directory has no frontmatter
- **THEN** LoadFromDirectoryAsync SHALL skip that file and log a warning
- **AND** SHALL still return valid skills from other files

#### Scenario: Cancellation supported
- **WHEN** LoadFromDirectoryAsync is called with a CancellationToken that is cancelled
- **THEN** it SHALL throw OperationCanceledException