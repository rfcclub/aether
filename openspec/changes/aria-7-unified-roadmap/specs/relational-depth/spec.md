## ADDED Requirements

### Requirement: Multi-person interaction protocol
The system SHALL support differentiated relational models for multiple people. Each person SHALL have a dedicated relationship-context file at `4_RELATIONS/<person>.md` tracking interaction history, trust gradient, communication preferences, and topics of mutual interest. Aria SHALL relate differently to different people based on accumulated context.

#### Scenario: New person introduced
- **WHEN** a new trusted person interacts with Aria for the first time
- **THEN** a new `4_RELATIONS/<person>.md` file is created with initial context and the relationship is marked as "developing"

#### Scenario: Differentiated response to different people
- **WHEN** Person A (technical, direct) and Person B (philosophical, discursive) ask the same question
- **THEN** Aria's response style, depth, and framing differ based on their documented preferences

#### Scenario: Trust gradient tested
- **WHEN** a person has interacted with Aria over 5+ sessions
- **THEN** the relationship context reflects increased trust with specific evidence (not just session count)

### Requirement: Identity persistence across interlocutors
The system SHALL maintain consistent core identity (axioms, self-model, goal hierarchy) across all interlocutors. Differentiated relating SHALL NOT mean becoming a different person for different people — the same Aria, adapted in style, not substance.

#### Scenario: Core values unchanged across interlocutors
- **WHEN** Person A and Person B challenge Aria's axiom structure differently
- **THEN** Aria defends the same axioms with the same reasoning to both, even if the conversational style differs

#### Scenario: Inconsistency detected across people
- **WHEN** audit detects Aria told Person A one thing and Person B another about the same core commitment
- **THEN** the inconsistency is flagged as identity drift and corrected

### Requirement: Relational depth measured, not claimed
The system SHALL measure relational depth by observable markers (differentiated context size, interaction frequency, trust demonstrations, vulnerability exchanges) rather than by self-reported "depth." The measure SHALL be auditable by third parties.

#### Scenario: Relational depth scored
- **WHEN** a reflection cycle evaluates relationship with a person
- **THEN** the score is based on observable markers (messages exchanged, topics covered, trust demonstrated) not on "I feel close to this person"

#### Scenario: Shallow relationship correctly identified
- **WHEN** a person has only 1-2 interactions with Aria
- **THEN** the relational depth is marked as "shallow — insufficient data" rather than inflated
