# dynamic-context-loading Specification

## Purpose

Define how Aether discovers and loads agent context files dynamically, replacing the hardcoded file-to-semantic-slot mapping with neutral file discovery.

## Requirements

### Requirement: All .md files discovered as context

The runtime SHALL discover all `.md` files in the agent directory and load them as context, without assigning semantic labels (constitution, identity, cognitive) to specific files.

#### Scenario: All markdown files loaded

- **WHEN** the agent directory contains `SOUL.md`, `AGENTS.md`, `AGENTS_GUARD.md`, `MEMORY.md`, and `2B/CORE_PARADOX.md`
- **THEN** all five files SHALL be discovered and loaded into the identity context section
- **THEN** no file SHALL be labeled "Constitution" or "Non-Negotiable" in the prompt

### Requirement: Fixed loading order

Discovered files SHALL be loaded in a deterministic order independent of filesystem traversal: `SOUL.md`, `IDENTITY.md`, `USER.md`, `AGENTS.md`, `AGENTS_GUARD.md`, `MEMORY.md`, followed by any remaining `.md` files alphabetically.

#### Scenario: Priority files loaded first

- **WHEN** files are discovered
- **THEN** `SOUL.md` SHALL appear before `AGENTS.md` in the prompt
- **THEN** `AGENTS_GUARD.md` SHALL appear before any alphabetically-discovered files

### Requirement: Dynamic files excluded

Files explicitly designated as dynamic (TASK_INBOX.md, HEARTBEAT.md, daily memory logs) SHALL NOT be loaded into the identity context section. They SHALL be loaded into the dynamic context section.

#### Scenario: TASK_INBOX excluded from identity context

- **WHEN** the agent directory contains `TASK_INBOX.md`
- **THEN** `TASK_INBOX.md` SHALL NOT appear in the Identity Context section
- **THEN** `TASK_INBOX.md` SHALL appear in the Dynamic Context section

### Requirement: Subdirectory recursion optional

File discovery MAY recurse into subdirectories (e.g., `2B/`). Whether recursion is enabled SHALL be configurable.

#### Scenario: Flat discovery excludes subdirectories

- **WHEN** recursion is disabled
- **THEN** only `.md` files directly in the agent directory SHALL be loaded
- **THEN** files in `2B/` SHALL NOT be loaded unless the LLM reads them via tools

#### Scenario: Recursive discovery includes subdirectories

- **WHEN** recursion is enabled
- **THEN** `.md` files in `2B/` and other subdirectories SHALL be discovered and loaded
