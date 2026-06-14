# LoomKit — Codex Agent Instructions

## Overview

LoomKit is a spec-driven design framework with TDD enforcement. Follow the workflow phases in order.

## Workflow

### BRAINSTORM — Explore the Problem

Before any implementation:
1. Ask one question at a time (Socratic dialogue)
2. Understand: problem, constraints, existing solutions
3. Propose 2-3 approaches with trade-offs
4. Output: `loomkit/changes/<name>/proposal.md`

### SPEC — Write Requirements

Write structured specifications:
- `### Requirement: <name>` with RFC 2119 keyword (SHALL/MUST/SHOULD)
- `#### Scenario: <name>` with WHEN/THEN format
- No OR in scenarios — split into separate scenarios
- Multiple AND clauses OK
- Output: `loomkit/changes/<name>/specs/<name>/spec.md`

### DESIGN — Technical Design

Write technical design:
- Architecture decisions + rationale
- Test strategy: map scenarios → test files
- File structure changes
- Output: `loomkit/changes/<name>/design.md`

### PLAN — TDD Implementation Plan

Write bite-sized implementation plan:
- Each Task has **Files** section (Create/Modify/Test + paths)
- Each Step is 2-5 min, has exact code
- NO placeholders, NO TBD, NO "implement later"
- TDD cycle per step: write failing test → verify fail → implement → verify pass → commit
- NO cross-task references ("similar to Task 1" is banned)
- Output: `loomkit/changes/<name>/tasks.md`

### TDD — Test-Driven Development

Iron Law: no code without failing test.
- RED: write failing test, watch it fail
- GREEN: write minimal code to pass
- REFACTOR: clean up, keep tests green
- If code written before test → delete code, write test first
- Map scenario ID → test file in `.traceability.yaml`

### VERIFY — Coverage Gate

Before archiving:
- Check scenario coverage (SHALL/MUST = mandatory)
- Run test suite
- Generate `.loomkit-verify.json`
- Must meet coverage threshold (default: 100%)
- Force possible with explicit reason

### ARCHIVE — Merge Specs

After verify passes:
- Merge delta specs into living specs
- Living specs at `loomkit/specs/<name>/spec.md`
- Archive metadata recorded
- Output: merged specs + `loomkit/archive/` metadata

## TDD Enforcement

- Test framework: vitest (default)
- Coverage threshold: 100% (mandatory scenarios)
- No implementation before failing test
- Delete code written before test

## Config

LoomKit reads `loomkit/config.yaml`:
```yaml
schema: spec-driven
tdd:
  framework: vitest
  enforce: true
  coverage_threshold: 100
context: |
  Project context here
rules:
  specs:
    - Every requirement needs ≥1 scenario
    - Use WHEN/THEN format
    - Use RFC 2119 keywords
  tasks:
    - Exact code in every step
    - No placeholders
```
