## ADDED Requirements

### Requirement: agent-delegation
The `agent_call` tool SHALL parse the target agent parameter and execute a secondary `AetherSoul` execution instance with the target agent's system prompt.

#### Scenario: Successful agent delegation
- **WHEN** Aura calls `agent_call` with agent "coda" and prompt "Run tests and return status"
- **THEN** A new child loop executes and the result string is returned as the tool's output to Aura

### Requirement: recursion-safeguard
The agent execution loop MUST trace the call stack depth and throw an error if the depth exceeds 3 nested calls.

#### Scenario: Preventing loop lockup
- **WHEN** Aura calls Coda, Coda calls Vesta, Vesta calls Maria, and Maria attempts to call Aura
- **THEN** The tool executor blocks the 4th call and returns a "Stack depth exceeded" error back to Maria

