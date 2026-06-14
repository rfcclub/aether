# LoomKit — Codex Integration

This adapter configures LoomKit for use with OpenAI Codex agents.

## Setup

1. Copy `AGENTS.md` content into your project's `AGENTS.md` (or merge if one exists)
2. Run `./generate-instructions.sh` to extract skill instructions into Codex-compatible format
3. Codex reads `AGENTS.md` automatically when working in the project

## Workflow Commands

Codex agents recognize these directives in AGENTS.md:

- `BRAINSTORM` — enter brainstorming phase
- `SPEC <name>` — write spec for capability
- `DESIGN <name>` — write technical design
- `PLAN <name>` — write TDD implementation plan
- `TDD` — enforce test-driven development
- `VERIFY <name>` — run coverage gate
- `ARCHIVE <name>` — merge verified change into living specs

## Differences from Claude Code Adapter

| Aspect | Claude Code | Codex |
|--------|-------------|-------|
| Instructions file | CLAUDE.md | AGENTS.md |
| Skills directory | ~/.claude/skills/ | Project-level AGENTS.md |
| Format | SKILL.md native | Inline in AGENTS.md |
