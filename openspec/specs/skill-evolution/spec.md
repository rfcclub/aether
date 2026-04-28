# skill-evolution Specification

## Purpose
TBD - created by archiving change skill-system. Update Purpose after archive.
## Requirements
### Requirement: Skill evolution records usage
The skill evolution system SHALL record each skill use with user message, whether it helped, and a confidence delta.

#### Scenario: Helpful skill records positive delta
- **WHEN** a skill "github-code-review" is used and marked as helpful
- **THEN** RecordUsageAsync SHALL add a record with ConfidenceDelta=+0.1

#### Scenario: Unhelpful skill records negative delta
- **WHEN** a skill "github-code-review" is used and marked as unhelpful
- **THEN** RecordUsageAsync SHALL add a record with ConfidenceDelta=-0.15

#### Scenario: Records capped at 100 per skill
- **WHEN** more than 100 usage records exist for a skill
- **THEN** the oldest records SHALL be removed to keep the count at 100

### Requirement: Skill evolution detects recidivism
When a skill has 3+ unhelpful uses in its last 10 uses AND the average confidence delta is negative, the skill evolution system SHALL generate a PromotionCandidate with source="recidivism".

#### Scenario: Recidivism triggers promotion candidate
- **WHEN** a skill has 4 unhelpful uses and 6 helpful uses in last 10
- **AND** the average confidence delta is negative (more harm than good)
- **THEN** GetRecidivismCandidatesAsync SHALL return one PromotionCandidate

#### Scenario: Good skills not flagged
- **WHEN** a skill has 8 helpful uses and 2 unhelpful in last 10
- **THEN** GetRecidivismCandidatesAsync SHALL NOT return a candidate for that skill

#### Scenario: Mixed but positive trend not flagged
- **WHEN** a skill has 4 unhelpful but overall average delta is still positive
- **THEN** GetRecidivismCandidatesAsync SHALL NOT return a candidate

### Requirement: Skill evolution exposes usage records
GetRecordsAsync(skillName, limit) SHALL return the most recent usage records for a given skill, ordered by recency.

#### Scenario: Records returned in reverse chronological order
- **WHEN** GetRecordsAsync("github-code-review", 20) is called
- **THEN** the returned list SHALL contain up to 20 records, most recent first

