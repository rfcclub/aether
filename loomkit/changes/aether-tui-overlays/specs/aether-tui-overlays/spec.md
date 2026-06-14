## ADDED Requirements

### Requirement: F4 Context Files Manager Overlay (MUST)

TUI client SHALL provide an interactive overlay when F4 is pressed to manage the workspace files currently attached to Aether's active context window.

#### Scenario: Open Context Manager Overlay
- **WHEN** the user presses F4 in normal chat mode
- **THEN** the application SHALL render the Context Files Manager overlay on top of the chat view
- **AND** the active input focus SHALL shift to the file manager list

#### Scenario: Add File to Context List
- **WHEN** the user is in the Context Manager overlay and presses 'a'
- **THEN** the TUI SHALL display an input prompt to enter a file path
- **AND** entering a valid path SHALL add the file to the context list
- **AND** the client SHALL transmit a WebSocket message with type "context_update" containing the updated files list

#### Scenario: Remove File from Context List
- **WHEN** the user selects a file in the Context Manager list and presses 'd'
- **THEN** the file SHALL be removed from the list
- **AND** the client SHALL transmit a WebSocket message with type "context_update" containing the updated files list

#### Scenario: Clear All Files from Context
- **WHEN** the user is in the Context Manager overlay and presses 'c'
- **THEN** all files SHALL be wiped from the list
- **AND** the client SHALL transmit a WebSocket message with type "context_update" containing an empty files list

---

### Requirement: F5 Socratic Brainstorming Wizard (SHOULD)

TUI client SHALL provide a step-by-step wizard panel when F5 is pressed to assist in structuring technical proposals.

#### Scenario: Open Brainstorming Wizard
- **WHEN** the user presses F5 in normal chat mode
- **THEN** the application SHALL open a step-by-step questionnaire panel
- **AND** the panel SHALL prompt for Motivation, Approach 1, Approach 2, and Trade-offs

#### Scenario: Inject Brainstorm Proposal
- **WHEN** the user completes all questionnaire steps
- **THEN** the TUI SHALL close the wizard panel
- **AND** it SHALL generate a structured markdown proposal and inject it directly into the input chat buffer

---

### Requirement: F6 TDD Template Injector (MUST)

TUI client SHALL provide a hotkey to instantly inject a standard TDD template into the input editor to enforce RED-GREEN-COMMIT cycles.

#### Scenario: Inject TDD Template into Input
- **WHEN** the user presses F6 in normal chat mode
- **THEN** the input text area SHALL be pre-populated with the standard RED-GREEN-REFACTOR checklist template

---

### Requirement: F7 Interactive Git Dashboard (MUST)

TUI client SHALL display a comprehensive split-pane dashboard when F7 is pressed to visualize and stage repository changes.

#### Scenario: Open Git Dashboard Pane
- **WHEN** the user presses F7 in normal chat mode
- **THEN** the application SHALL display a full-screen Git Dashboard
- **AND** the left column SHALL display staged, unstaged, and untracked files
- **AND** the right column SHALL display the inline color-coded diff of the selected file

#### Scenario: Toggle File Staging Status
- **WHEN** the user selects a file in the Git Dashboard and presses the Spacebar
- **THEN** the client SHALL send a WebSocket message with type "stage_file" containing the file path and new target state
- **AND** the Git Dashboard SHALL refresh its file lists and inline diff views immediately

---

### Requirement: Active LLM Stream Interruption (MUST)

TUI client and C# Backend SHALL support real-time interruption of active LLM generations when the user cancels.

#### Scenario: Trigger Stream Cancellation
- **WHEN** the user presses Esc while the LLM is actively typing/streaming a response
- **THEN** the TUI client SHALL immediately stop the typing animation, lock the current output, and return to normal idle mode
- **AND** the client SHALL transmit a WebSocket frame of type "cancel" to the server
- **AND** the server SHALL immediately cancel the CancellationToken for the active LLM process, terminating all remote LLM calls and local subprocesses
