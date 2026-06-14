# ws-list-models — Specification

> Capability: Backend handles `list_models` WS message and returns structured provider+model list.

---

## ADDED Requirements

### Requirement: `list_models` message type handled in WebSocketChannel

The system SHALL process `{"type":"list_models"}` received from any connected WebSocket client and respond with a structured model list to that client only.

#### Scenario: Models available

- **WHEN** a WS client sends `{"type":"list_models"}`
- **THEN** the backend SHALL respond to that client only with:
  ```json
  {
    "type": "models",
    "current": "<effective_model>",
    "think_effort": "<effort_or_null>",
    "providers": [
      {"name": "<provider_name>", "models": ["<model1>", "<model2>"]}
    ]
  }
  ```
- **THEN** providers SHALL be grouped from `ProviderRouter.GetAvailableModels()` result
- **THEN** `current` SHALL be `ProviderRouter.EffectiveModel`

#### Scenario: No models configured

- **WHEN** `GetAvailableModels()` returns an empty list
- **THEN** the backend SHALL respond with `{"type":"models","current":"none","think_effort":null,"providers":[]}`
- **THEN** no error SHALL be sent

#### Scenario: Other connected clients not notified

- **WHEN** client A sends `list_models` and clients B and C are also connected
- **THEN** only client A SHALL receive the `models` response
- **THEN** clients B and C SHALL receive nothing

---

### Requirement: `list_models` does not modify state

The system SHALL treat `list_models` as a read-only query; it SHALL NOT change model selection or any server state.

#### Scenario: Repeated calls are idempotent

- **WHEN** the same client sends `list_models` twice
- **THEN** the backend SHALL respond twice with the same data
- **THEN** no state change SHALL occur between calls
