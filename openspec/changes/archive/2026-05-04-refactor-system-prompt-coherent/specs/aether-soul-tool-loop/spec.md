# aether-soul-tool-loop Specification Delta

## MODIFIED Requirements

### Requirement: Tool Definitions Provided to LLM

`AetherSoul` MUST include the definitions of all built-in tools in every `LlmRequest`. The tool set SHALL remain the 6 built-in tools: `bash`, `read`, `glob`, `grep`, `write`, and `edit`.

#### Scenario: Tools included in request

- **WHEN** `ProcessAsync` builds an `LlmRequest`
- **THEN** `request.Tools` contains definitions for `bash`, `read`, `glob`, `grep`, `write`, and `edit`

## ADDED Requirements

### Requirement: Coherent system prompt structure

`AetherSoul.BuildSystemPrompt()` SHALL produce a 3-section prompt (Identity Context, Behavior Instructions, Dynamic Context) with a cache boundary marker, as specified in `coherent-system-prompt`.

#### Scenario: Three-section prompt built

- **WHEN** `BuildSystemPrompt()` is called with valid context
- **THEN** the output SHALL contain Identity Context, Behavior Instructions, and Dynamic Context sections separated by a cache boundary marker

### Requirement: BuildSystemPrompt simplified parameters

`BuildSystemPrompt()` SHALL accept reduced parameters: `identityContext` (string), `dynamicContext` (string), and optional `skillContext` (SkillContext?). The previous 8-parameter signature is removed.

#### Scenario: New signature compiles

- **WHEN** `BuildSystemPrompt(identityContext, dynamicContext, skillContext)` is called
- **THEN** it SHALL return a valid system prompt string
- **THEN** the method SHALL have exactly 3 parameters

### Requirement: System prompt includes anti-hallucination directive

The system prompt SHALL include the directive "CRITICAL — You MUST Use Tools To Read Files" requiring `read` tool use before describing file contents.

#### Scenario: Anti-hallucination directive present

- **WHEN** the system prompt is built
- **THEN** it SHALL include "You MUST call the `read` tool to read it"
- **THEN** it SHALL include "You are FORBIDDEN from describing, assuming, or fabricating file contents"

## REMOVED Requirements

### Requirement: Intermediate Messages Persisted

**Reason**: This requirement is unchanged by the refactor and remains in the base spec. It is listed here only to confirm it is not affected.

**Migration**: No action needed.

### Requirement: Iteration Guard

**Reason**: This requirement is unchanged by the refactor and remains in the base spec.

**Migration**: No action needed.
