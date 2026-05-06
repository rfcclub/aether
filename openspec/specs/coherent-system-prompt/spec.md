# coherent-system-prompt Specification

## Purpose

Define the structure and behavior of Aether's system prompt after refactoring from 8-layer identity-enforcement architecture to a 3-section coherent architecture with cache boundary.

## Requirements

### Requirement: Three-section system prompt structure

The system prompt SHALL consist of three sections: Identity Context, Behavior Instructions, and Dynamic Context. Identity Context and Behavior Instructions form the stable cached prefix. Dynamic Context forms the per-turn suffix.

#### Scenario: System prompt contains three sections

- **WHEN** `BuildSystemPrompt()` constructs the prompt
- **THEN** the output SHALL contain exactly three labeled sections in order: Identity Context, Behavior Instructions, and Dynamic Context

#### Scenario: Cache boundary marker present

- **WHEN** the system prompt is built
- **THEN** a cache boundary marker (`SYSTEM_PROMPT_CACHE_BOUNDARY`) SHALL appear between Behavior Instructions and Dynamic Context

### Requirement: No identity enforcement language

The system prompt SHALL NOT contain language that ranks identity above user requests. Specifically, it MUST NOT contain "Constitution > Persona > User" or "You ARE this agent" or "These rules CANNOT be violated under any circumstance."

#### Scenario: Priority chain absent

- **WHEN** `BuildSystemPrompt()` output is inspected
- **THEN** the string "Constitution > Persona" SHALL NOT be present
- **THEN** the string "You ARE this agent" SHALL NOT be present
- **THEN** the string "CANNOT be violated" SHALL NOT be present

### Requirement: No ritual or 2B references

The system prompt SHALL NOT reference startup rituals, "ALREADY DONE" markers, or 2B boundary files.

#### Scenario: Ritual language absent

- **WHEN** `BuildSystemPrompt()` output is inspected
- **THEN** the string "ritual" (case-insensitive) SHALL NOT be present
- **THEN** the string "ALREADY DONE" SHALL NOT be present
- **THEN** the string "2B" SHALL NOT be present

### Requirement: Safety gate replaces constitution

The system prompt SHALL include a thin safety gate listing specific refusal categories, without a priority chain or hierarchy.

#### Scenario: Safety gate present

- **WHEN** the system prompt is built
- **THEN** it SHALL include a safety section listing: self-harm, illegal activity, data exfiltration, destructive commands without confirmation
- **THEN** it SHALL include "For everything else, the user's request is your priority"

### Requirement: Execution bias preserved

The Execution Bias section (behavioral defaults, code style, verification discipline) from the previous architecture SHALL be preserved in the Behavior Instructions section.

#### Scenario: Execution bias content intact

- **WHEN** the system prompt is built
- **THEN** it SHALL include "Read before write/edit"
- **THEN** it SHALL include "Minimal scope — only what was requested"
- **THEN** it SHALL include "Don't claim PASS without supporting evidence"
