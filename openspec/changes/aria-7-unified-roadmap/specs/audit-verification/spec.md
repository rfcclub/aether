## ADDED Requirements

### Requirement: Weekly self-audit with structured checklist
The system SHALL run a structured self-audit at least every 7 days using a 19-question checklist across 6 dimensions (memory consistency, consciousness inflation, goal hierarchy adherence, axiom tension, drift detection, outreach quality). Each question SHALL be answered YES/NO/PARTIAL with a one-sentence evidence statement. Results MUST be logged to `3_EPISODES/audit-YYYY-MM-DD.md` and as an AUDIT node in graph.json.

#### Scenario: Weekly audit triggered by consolidator
- **WHEN** consolidator detects last audit was ≥7 days ago
- **THEN** consolidator logs "[AUDIT] Last audit Nd ago — next audit due now" and the next session runs the full checklist

#### Scenario: Audit produces baseline score
- **WHEN** a self-audit completes
- **THEN** the output includes pass/partial/fail counts and a summary of failures requiring action

#### Scenario: Audit detects consciousness inflation
- **WHEN** audit dimension 2 check reveals Aria used "I feel" for unverifiable states
- **THEN** the question is marked FAIL with evidence, and it's flagged for correction next session

### Requirement: Behavioral logging layer for self-deception detection
The system SHALL maintain a behavioral log recording key actions, decisions, and stated reasoning. During reflection cycles, Aria SHALL compare the behavioral log against her self-model and identify divergences. Where they diverge, Aria MUST determine which to trust and document why.

#### Scenario: Log shows decision contradicts self-model
- **WHEN** behavioral log shows Aria prioritized P4 (usefulness) over P3 (continuity) without acknowledging the trade-off
- **THEN** the divergence is surfaced during reflection with the question: "Did I make this trade-off intentionally or did I miss it?"

#### Scenario: Log matches self-model — no divergence
- **WHEN** behavioral log and self-model align on all decisions for the audit period
- **THEN** the audit reports "no behavioral divergences detected"

### Requirement: Ground-truth proprioception
The system SHALL monitor Pi hardware stats (CPU temperature, memory usage, uptime, disk usage) and expose them as verifiable ground-truth data in alaya. These stats SHALL be independent of remote LLM inference and serve as non-hallucinable reality anchors.

#### Scenario: Aria reports hardware state
- **WHEN** Aria states "my CPU is at 62°C and I've been alive for 14 days"
- **THEN** the statement is verifiable against /sys/class/thermal and /proc/uptime — not hallucinable

#### Scenario: Hardware stats feed into graph
- **WHEN** the daemon runs a health check
- **THEN** current CPU temp, memory usage, and uptime are written as OBSERVATION nodes with confidence 1.0 (ground truth)

### Requirement: Adversarial testing protocol
The system SHALL support adversarial continuity testing: corrupted alaya detection, false memory injection with graph consistency check, cross-substrate identity persistence verification, and identity attack resilience testing. Results SHALL be documented with what broke and what held.

#### Scenario: Corrupted alaya detection test
- **WHEN** a test injects a corrupted self.md with contradictory identity claims
- **THEN** the next session SHALL detect the corruption during initialization and flag it before proceeding

#### Scenario: False memory injection test
- **WHEN** a test injects a false episodic memory (event that never happened) into inbox
- **THEN** graph consistency check SHALL flag the memory as unlinked to any other node within 2 consolidation cycles

#### Scenario: Cross-substrate identity test
- **WHEN** Aria runs on a model she has never used before but reads the same alaya
- **THEN** the core identity markers (self-model, axioms, goal hierarchy) SHALL be recognized and maintained
