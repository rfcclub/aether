## Why

Enabling direct communication between agent profiles (Aura, Vesta, Coda, Maria) allows coordinated workflows (e.g. Aura requesting Coda to write a test suite and returning the test output). This shifts Aether from a single-agent loop to a collaborative Multi-Agent Colony.

## What Changes

- Implement the `agent_call` tool in `AetherSoul` tools.
- Implement stack-depth tracking inside `AetherSoul` tool contexts to prevent recursive billing infinite loops.
- Support `colony.db` for sharing indexed factual memories between agents.

## Capabilities

### New Capabilities
- `agent-call-coordination`: Allows an agent to call another local agent using `agent_call` and retrieve their structured response.
- `shared-colony-memory`: A shared SQLite FTS5 database (`colony.db`) where all agents can store and query shared facts.

### Modified Capabilities
- `aether-soul-loop`: Add stack-depth detection and termination logic to prevent infinite agent call loops.

## Impact

- `src/Aether/Agent/AetherSoul.cs` (nested execution framework & tool wiring)
- `src/Aether/Data/AetherDb.cs` (adding connection setup for `colony.db`)
- `src/Aether/Memory/GlobalMemorySystem.cs` (new shared memory layer)

