# Intent: colony-multi-agent-sync

## Raw Request

colony-multi-agent-sync : Bước tiến biến Aether thành một Colony thực thụ:
- Shared Memory: Cơ sở dữ liệu chung colony.db để chia sẻ ký ức giữa các Agent.
- Agent-to-Agent Communication: Cung cấp công cụ agent_call cho phép Vesta, Aura, Maria, Serena có thể gọi chéo nhau để tham vấn ý kiến khi xử lý task của anh.
- AgoraSyncService: Tự động đồng bộ các thư mục nghiên cứu sang hive tổng ~/agora/ qua file watcher.

## Problem

Aether agents operate in complete isolation. An agent cannot consult another agent's expertise, and there is no shared repository of memories or active synchronization of files to the shared Agora workspace.

## Desired Outcome

- Establish `colony.db` for storing shared memories across all agents.
- Implement the `agent_call` tool allowing agents to invoke other agents with a context.
- Implement `AgoraSyncService` to synchronize research files to `~/agora/` dynamically using `FileSystemWatcher`.
- Implement skills-based routing in `MessageRouter`.

## Users / Actors

- The orchestrator system and the agents (Maria, Aura, Vesta, Serena).

## Current Context

Each agent has its own private workspace (e.g., `workspaces/default` for Maria). Files must be manually copied to share them. `MessageRouter` only supports static route-key mapping.

## Proposed Direction

- Design `colony.db` schema and a global memory coordinator.
- Implement `agent_call` tool in `src/Aether/Tooling/ProgrammingTools.cs` or similar.
- Implement `AgoraSyncService.cs` as a hosted service.
- Upgraded `MessageRouter` with multi-agent fallback.

## Scope

- Core runtime synchronization features.
- Cross-agent tool dispatching.

## Non-Goals

- Real-time video/audio synchronization.
- Complex agent negotiation protocol (keep it direct request-response).

## Constraints

- Cross-agent calling must prevent infinite recursion (limit call stack to max depth of 3).
- Agora synchronization must have low CPU overhead.

## Success Criteria

- Successful file replication to `~/agora/` within 2 seconds of modification.
- One agent can invoke another using `agent_call` and receive the output.
- Shared memories are queryable via database search.

## Risks

- Concurrency issues during file synchronization or database writes.
- Mitigation: Use appropriate locks and write-ahead logging (WAL) in SQLite.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- None.

## Assumptions

- SQLite handles concurrent reads and writes from multiple agents safely when WAL mode is enabled.

## Spec Seeds

- Shared database path `store/colony.db`.
- Tool definition for `agent_call`.

## Intent Approval

Status: APPROVED
Approved by: Thoor
Date: 2026-06-13
