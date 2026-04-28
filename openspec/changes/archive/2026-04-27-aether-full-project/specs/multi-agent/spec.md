## ADDED Requirements

### Requirement: Agent selected by channel source via configuration
The gateway SHALL route each inbound message to the agent named in `appsettings.json` under `gateway.agents.<source>`. If no mapping exists, the `default` agent SHALL be used.

#### Scenario: Channel-specific agent configured
- **WHEN** `gateway.agents.telegram = "aria"` is configured and a Telegram message arrives
- **THEN** the gateway SHALL dispatch the message to the `AetherSoul` instance registered as `"aria"`

#### Scenario: No mapping for source, fallback to default
- **WHEN** no agent mapping exists for the channel source
- **THEN** the gateway SHALL dispatch to the agent named `"default"`

#### Scenario: Named agent not registered
- **WHEN** configuration references an agent name that is not registered in DI
- **THEN** the gateway SHALL log an error and respond with a static error message to the channel

### Requirement: Named agents have isolated session contexts
Each named agent SHALL maintain its own session namespace so sessions from different agents do not overlap.

#### Scenario: Same chat ID, different agents
- **WHEN** channel `telegram` and channel `websocket` both receive a message from group `"chat-123"` but route to different agents
- **THEN** each agent SHALL maintain a separate session (different `session_id`) in the database

#### Scenario: Agent shares same SQLite database
- **WHEN** multiple named agents are active
- **THEN** all agents SHALL share the same `aether.db` file with sessions disambiguated by `group_folder` prefix (`<agent_name>/<group_folder>`)

### Requirement: Agent definitions declarable in configuration
Agents SHALL be definable in configuration with optional per-agent settings (model override, memory path, skill directory).

#### Scenario: Agent with custom model
- **WHEN** `agents.aria.model = "openrouter/kimi-k2.6"` is configured
- **THEN** the `aria` agent's `AetherSoul` SHALL use that model for LLM calls, overriding the global default
