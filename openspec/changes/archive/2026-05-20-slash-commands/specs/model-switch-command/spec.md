## ADDED Requirements

### Requirement: /model shows current model
The system SHALL return the current primary model and fallback list when `/model` is sent with no arguments.

#### Scenario: Show model chain
- **WHEN** user sends "/model"
- **THEN** response shows current primary model and fallback list, e.g., "Model: crof-ai/glm-5.1 (fallbacks: google/gemini-3.1-pro-preview)"

### Requirement: /model <name> switches model
The system SHALL update `ProviderRouter.ModelChain` with the specified model as primary, preserving fallbacks from agent config. The change SHALL take effect on the next LLM call without requiring a restart.

#### Scenario: Switch primary model
- **WHEN** user sends "/model claude-sonnet-4-6"
- **THEN** the primary model is updated to "claude-sonnet-4-6" and response confirms "Model changed to: claude-sonnet-4-6"

#### Scenario: Switch to unknown model
- **WHEN** user sends "/model nonexistent-model"
- **THEN** response warns "Model 'nonexistent-model' not found in provider config" but still sets it (provider resolution happens at call time)
