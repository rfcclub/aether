# session-history-trimming Specification

## Purpose
TBD - created by archiving change session-history-trimming. Update Purpose after archive.
## Requirements
### Requirement: Token Estimation

The system MUST provide a token estimator that returns an integer token count estimate for a string.

#### Scenario: Character-based estimate
- **WHEN** `CharTokenEstimator.Estimate` is called with a string
- **THEN** it returns `text.Length / 4 + 1` as the estimated token count

### Requirement: History Trimming

The system MUST trim message history to fit within a token budget by dropping oldest messages first.

#### Scenario: History fits within budget
- **WHEN** total estimated tokens for all messages is below `budget`
- **THEN** all messages are returned unchanged

#### Scenario: History exceeds budget
- **WHEN** total estimated tokens for all messages exceeds `budget`
- **THEN** oldest messages are dropped until the remaining list fits within the budget

#### Scenario: Single message exceeds budget
- **WHEN** even one message's estimated token count exceeds `budget`
- **THEN** only the most recent message is returned (never return empty history)

### Requirement: System Prompt Exclusion

The trimming budget MUST account for the system prompt tokens separately.

#### Scenario: System prompt tokens deducted from budget
- **WHEN** `HistoryTrimmer.Trim` is called with `systemPromptTokens > 0`
- **THEN** the effective budget for history is `budget - systemPromptTokens`

### Requirement: AetherSoul Applies Trim

`AetherSoul.ProcessAsync` MUST apply history trimming before building the `LlmRequest`.

#### Scenario: Trimmed history is used in request
- **WHEN** raw history exceeds the token budget
- **THEN** only trimmed history messages are included in `LlmRequest.Messages`

#### Scenario: Budget configured from appsettings
- **WHEN** `agent.history_token_budget` is set in `appsettings.json`
- **THEN** that value is used as the trimming budget

