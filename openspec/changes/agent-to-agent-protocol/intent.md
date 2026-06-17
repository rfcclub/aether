# Intent: agent-to-agent-protocol

## Raw Request

"còn gì em có thể làm để có thể hoàn thiện Aether ? Như MCP support, tool support, plugin, A2A protocol, hay những gì tà đạo em nghĩ ra ?" -> "dùng LoomKit tạo intent và specs rồi bỏ trong thư mục openspec, và làm B, C, A, D" (A: Agent-to-Agent protocol)

## Problem

Aether currently lacks structured communication between different agent profiles (Aura, Vesta, Coda, Maria). They function in isolation, unable to delegate tasks or collaborate on complex issues. For example, Aura cannot query Vesta's architecture guidelines, and Coda cannot ask Aura to write infrastructure scripts.

## Desired Outcome

Aether agents can:
1. Message each other dynamically using an `agent_call` tool, triggering sub-agent processes.
2. Synchronize research directories and share memory pools via a common database state (`colony.db`).
3. Delegate tasks recursively without hitting infinite loops (recursion/depth safeguards).

## Users / Actors

- **Developer / Agent:** Triggers a coordination task where Agent A invokes Agent B.
- **Agent A (e.g. Aura):** Delegates code testing to Agent B (e.g. Coda) and receives test results as a tool output.

## Current Context

- `MessageRouter.cs` manages inbound channels.
- `AetherSoul.cs` runs the core agent loop.
- Hive directory structure exists under `~/agora/`.

## Proposed Direction

- Define an `agent_call` tool that spins up a separate instance of `AetherSoul` configured with the target agent's profile (System Prompt, skills).
- Establish `colony.db` to serve as a Shared Memory layer where agents can write to a global SQLite FTS5 index.
- Implement stack-depth tracking in `AetherSoul` tool call context: if depth exceeds a limit (e.g., 3), the agent call is rejected with a stack overflow error.

## Scope

- Implementation of the `agent_call` tool interface.
- Shared memory database access (`colony.db`) and utility tools (`memory_share`, `memory_pull`).
- Safe sub-agent invocation via WebSocket or local in-process thread spawning.
- Stack depth / loop prevention checks.

## Non-Goals

- Complex external server synchronization (only handles local agents within `~/agora/` hive).
- Real-time video/audio conferencing between agents.

## Constraints

- Agents must run asynchronously without blocking the parent agent's channel completely.
- Resource isolation (memory limits) to prevent system locks.

## Success Criteria

- Aura can execute `agent_call` targeting Coda to verify C# code correctness and obtain a "Success/Fail" result.

## Risks

- Multi-agent lockups and recursion loop billing costs.
- Mitigation: Enforce strict token and recursion limits on all child agent invocations.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- Should the conversation history of the child agent be merged into the parent? -> No, kept separate; parent only receives the final structured answer from the tool call.

## Assumptions

- Agent profiles (Aura, Vesta, Coda, Maria) are configured in `CONFIGURATION.md` or `.aether.json`.

## Spec Seeds

- Tool definition:
  ```json
  {
    "name": "agent_call",
    "description": "Delegates a task to another specialist agent (coda, vesta, maria, aura).",
    "parameters": {
      "type": "object",
      "properties": {
        "agent": { "type": "string", "enum": ["coda", "vesta", "maria", "aura"] },
        "prompt": { "type": "string" }
      },
      "required": ["agent", "prompt"]
    }
  }
  ```

## Intent Approval

Status: APPROVED

Approved by: Thoor
Date: 2026-06-16

