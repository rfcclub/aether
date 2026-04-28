# skill-registry Specification

## Purpose
TBD - created by archiving change skill-system. Update Purpose after archive.
## Requirements
### Requirement: Skill registry stores and retrieves skills
The skill registry SHALL store skill definitions by name and allow retrieval, listing, and checking existence.

#### Scenario: Register and resolve
- **WHEN** a skill definition is registered with name "github-code-review"
- **THEN** Resolve("github-code-review") SHALL return that skill definition

#### Scenario: Unregister removes skill
- **WHEN** a skill "github-code-review" is registered and then unregistered
- **THEN** Resolve("github-code-review") SHALL return null
- **AND** HasSkill("github-code-review") SHALL return false

#### Scenario: List returns all registered skills
- **WHEN** multiple skills are registered
- **THEN** List() SHALL return all skill names sorted alphabetically

#### Scenario: HasTool returns true for registered skills
- **WHEN** a skill "github-code-review" is registered
- **THEN** HasSkill("github-code-review") SHALL return true
- **AND** HasSkill("nonexistent") SHALL return false

### Requirement: Skill registry supports auto-detection via keyword matching
The skill registry SHALL provide FindMatching(userMessage) that returns skills whose description or when_to_use fields share words with the user message above a 0.3 relevance threshold.

#### Scenario: Keyword overlap triggers match
- **WHEN** user says "review my pull request" and a skill has description "code review tool"
- **THEN** "review" and "pull" and "request" overlap with skill words
- **AND** FindMatching SHALL return that skill

#### Scenario: No match below threshold
- **WHEN** user says "hello how are you" and no skill has relevant keywords
- **THEN** FindMatching SHALL return an empty list

### Requirement: Empty or whitespace skill name is rejected
The skill registry SHALL throw ArgumentException if Register is called with a null or whitespace skill name.

#### Scenario: Null name throws
- **WHEN** Register is called with null
- **THEN** it SHALL throw ArgumentException

#### Scenario: Whitespace name throws
- **WHEN** Register is called with "   " (whitespace only)
- **THEN** it SHALL throw ArgumentException

