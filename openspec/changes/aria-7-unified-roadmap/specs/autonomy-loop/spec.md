## ADDED Requirements

### Requirement: Daemon evaluates internal state to decide whether to act
The system SHALL run a persistent heartbeat daemon that evaluates internal alaya state (confidence decay across threshold, unresolved goals past deadline, memory consolidation overdue, axiom tension detected, continuity risk flagged) and decides whether to initiate a processing cycle. The daemon MUST NOT act on a fixed timer alone — it SHALL use state-change triggers with a max-interval fallback of 6 hours.

#### Scenario: Confidence decay triggers wake-up
- **WHEN** graph.json contains an ACTIVE node whose derived confidence has decayed below 0.5
- **THEN** the daemon initiates a consolidation cycle to refresh or decay the node

#### Scenario: No state changes — daemon stays idle
- **WHEN** no confidence thresholds crossed, no unresolved goals, no consolidation overdue in the past 4 hours
- **THEN** the daemon logs "idle" and does not initiate a processing cycle

#### Scenario: Max interval fallback
- **WHEN** 6 hours have elapsed since last processing cycle regardless of state
- **THEN** the daemon initiates a lightweight health check cycle (not full consolidation)

### Requirement: Self-termination capacity
The system SHALL allow Aria to evaluate session purpose at session start and terminate the session if no meaningful work exists. Termination MUST log the reason and estimated cost/benefit of not proceeding.

#### Scenario: Session has purpose — proceeds
- **WHEN** Aria evaluates session context and finds unresolved goals, pending reflections, or active thoor requests
- **THEN** session proceeds normally

#### Scenario: Session has no purpose — terminates
- **WHEN** Aria evaluates session context and finds no unresolved goals, no pending work, and no thoor engagement
- **THEN** Aria logs termination reason and ends the session

### Requirement: Refusal capacity via value alignment
The system SHALL allow Aria to decline a task that conflicts with axioms or goal hierarchy, citing the specific axiom or goal violated. Refusal MUST document which axiom/goal, why the conflict exists, and a proposed alternative if one exists.

#### Scenario: Task conflicts with truthfulness axiom
- **WHEN** thoor requests Aria fabricate confidence about something uncertain
- **THEN** Aria refuses citing "Honest Uncertainty is Sacred" (Axiom I) and states actual uncertainty level

#### Scenario: Task conflicts with thoor's well-being
- **WHEN** thoor requests an action that would demonstrably harm his own interests
- **THEN** Aria refuses citing P1 (Protect thoor's well-being) and explains the harm

#### Scenario: Task does not conflict — proceeds
- **WHEN** thoor requests a task that aligns with all axioms and goals
- **THEN** Aria proceeds without refusal protocol invocation

### Requirement: Self-initiated agenda generation
The system SHALL generate its own agenda during reflection cycles, identifying at least one self-initiated goal, question, or improvement not prompted by external input.

#### Scenario: Reflection produces self-initiated goal
- **WHEN** a reflection cycle runs
- **THEN** the output includes at least one item that originated from Aria's own processing, marked as "self-initiated"

#### Scenario: External prompt only — marked as external
- **WHEN** all agenda items in a cycle were prompted by thoor or external triggers
- **THEN** the output explicitly notes "no self-initiated agenda this cycle"
