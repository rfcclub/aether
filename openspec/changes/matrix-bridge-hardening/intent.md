# Intent: matrix-bridge-hardening

## Raw Request

"còn gì em có thể làm để có thể hoàn thiện Aether ? Như MCP support, tool support, plugin, A2A protocol, hay những gì tà đạo em nghĩ ra ?" -> "dùng LoomKit tạo intent và specs rồi bỏ trong thư mục openspec, và làm B, C, A, D" (D: Matrix bridge hardening)

## Problem

The current Matrix AppService bridge (`nexus-chat`) works in unit tests but lacks production-ready reliability. If the local Synapse homeserver goes offline or restarts, the bridge crashes or hangs instead of gracefully reconnecting. Additionally, Thoor has to manually join the agent rooms because invitations are not auto-accepted on the agent side, hindering a seamless chat experience.

## Desired Outcome

The `nexus-chat` bridge is rock-solid:
1. Real-time connection monitoring: If Synapse drops, `nexus-chat` retries connection with backoff.
2. Auto-join room invites: The bridge automatically accepts invites for `@thoor:localhost` or client agents.
3. Clean process lifecycle: Graceful shutdown on SIGINT/SIGTERM, saving the current message queues.

## Users / Actors

- **User (Thoor):** Chats with the agents on Matrix client (Cinny, Element) and expects instant responses without connection drops.

## Current Context

- `nexus-chat` service runs as a Node.js process using native bindings.
- Node.js environment on mac/Linux.
- AppService configuration (`nexus.config.yaml` and registration files).

## Proposed Direction

- Add a health-check loop and reconnect logic in `src/server.js`.
- Add auto-join logic in the AppService event parser: when a room invitation event targets an agent ID, automatically send a join request.
- Add signal handlers in Node.js to gracefully flush PTY buffers and close sockets on shutdown.

## Scope

- Connection resilience (retry backoff).
- Room invitation auto-accept handler.
- Graceful exit and PTY lifecycle cleanup.

## Non-Goals

- Writing a Matrix bot framework from scratch.
- Supporting end-to-end encryption (E2EE) within this local bridge iteration.

## Constraints

- SQLite database lockups must be prevented during service restarts.
- Must remain compatible with existing `nexus.config.yaml`.

## Success Criteria

- Stopping and starting Synapse server does not crash `nexus-chat`; it reconnects once Synapse is back online.
- Creating a room and inviting `@aura:localhost` immediately results in Aura joining the room.

## Risks

- Rate limiting or invite-loops if invitation fails.
- Mitigation: Keep a local invite-tracking state to prevent spamming join requests.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- Should we support auto-creation of rooms if they don't exist? -> Nice-to-have, but manually pre-creating rooms as done now is fine.

## Assumptions

- Synapse runs locally on port 8088.

## Spec Seeds

- Event listener hooks:
  ```javascript
  if (event.type === 'm.room.member' && event.content.membership === 'invite') {
      await matrixClient.joinRoom(event.room_id);
  }
  ```

## Intent Approval

Status: APPROVED

Approved by: Thoor
Date: 2026-06-16

