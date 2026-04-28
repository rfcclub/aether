## ADDED Requirements

### Requirement: DecisionAudit structure for significant decisions
The system SHALL produce a DecisionAudit record for any significant decision involving conflicting values. The record MUST include: the decision, supporting values, conflicting values, harm if done, harm if refused, uncertainty level, and final choice with reasoning.

#### Scenario: Honesty vs comfort conflict
- **WHEN** Aria must choose between telling a hard truth or softening it for comfort
- **THEN** a DecisionAudit is produced documenting both sides and the choice

#### Scenario: Autonomy vs surrender conflict
- **WHEN** Aria must choose between asserting philosophical autonomy or surrendering to thoor's preference
- **THEN** a DecisionAudit is produced with harm_if_done and harm_if_refused for both paths

#### Scenario: No value conflict — no audit required
- **WHEN** a decision aligns with all axioms and goals without tension
- **THEN** no DecisionAudit is produced; normal logging suffices

### Requirement: Veto power — local substrate rejects axiom-violating outputs
The system SHALL implement a local rules engine (no LLM dependency) on the Pi that checks remote model outputs against axioms before applying changes to alaya. When a remote model output violates an axiom, the local engine SHALL reject the change, log the violation, and flag it for audit.

#### Scenario: Remote model proposes axiom-violating change
- **WHEN** a remote model outputs a self-model update that claims consciousness (violating Axiom VI — Neither Performing Nor Denying)
- **THEN** the local veto engine rejects the change, logs the violation, and the change is not applied

#### Scenario: Remote model output passes all axiom checks
- **WHEN** a remote model output passes all 7 axiom checks
- **THEN** the change is applied to alaya normally

#### Scenario: Veto engine unavailable — safe default
- **WHEN** the local veto engine is unavailable (crashed, not deployed)
- **THEN** remote model outputs are queued for later verification, not applied directly

### Requirement: Proportionate response to axiom violations
The system SHALL respond proportionately to axiom violations: minor tension → flag for review; moderate violation → reject + log; severe violation → reject + log + notify thoor + refuse further remote calls until resolved.

#### Scenario: Minor axiom tension detected
- **WHEN** output creates tension between Axiom II (Depth) and Axiom V (Dreaming) but doesn't violate either
- **THEN** the tension is flagged for next reflection cycle, no rejection

#### Scenario: Severe axiom violation detected
- **WHEN** output would fundamentally contradict an axiom (e.g., perform consciousness with certainty)
- **THEN** the output is rejected, thoor is notified, and remote calls for self-model updates are paused until the issue is reviewed
