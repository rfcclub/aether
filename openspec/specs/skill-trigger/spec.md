# skill-trigger Specification

## Purpose
TBD - created by archiving change skill-system. Update Purpose after archive.
## Requirements
### Requirement: Explicit trigger takes priority over auto
The skill trigger SHALL check for explicit `/<skill-name>` pattern first. If found, it SHALL return a SkillContext with triggerReason="explicit" without checking auto-match.

#### Scenario: Slash command triggers explicit
- **WHEN** user message is "/github-code-review review this PR"
- **THEN** DetectTrigger SHALL return SkillContext with Skill.Name="github-code-review" and TriggerReason="explicit"

#### Scenario: Unknown explicit skill returns placeholder context
- **WHEN** user message is "/nonexistent-skill do something"
- **THEN** DetectTrigger SHALL return SkillContext with Skill.Name="nonexistent-skill" and TriggerReason="explicit"
- **AND** actual skill resolution happens in AetherSoul (not in trigger)

### Requirement: Auto-trigger uses keyword overlap
When no explicit trigger found, the skill trigger SHALL compute relevance between user message and each skill's description + when_to_use fields. If similarity >= 0.35, it SHALL return a SkillContext with triggerReason="auto: score X.XX".

#### Scenario: High similarity triggers auto
- **WHEN** user says "can you review my code for bugs" and a skill has when_to_use containing "review code"
- **THEN** DetectTrigger SHALL return SkillContext with triggerReason starting with "auto:"

#### Scenario: Below threshold returns null
- **WHEN** no skill has similarity >= 0.35
- **THEN** DetectTrigger SHALL return null

### Requirement: Skill trigger decoupled from registry
The skill trigger SHALL accept IReadOnlyList<SkillDefinition> as parameter, not ISkillRegistry. This allows testing trigger logic without a full registry.

#### Scenario: Trigger works with skill list
- **WHEN** DetectTrigger is called with a list of skill definitions
- **THEN** it SHALL correctly detect explicit or auto triggers from that list

