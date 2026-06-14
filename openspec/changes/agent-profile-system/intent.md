# Intent: agent-profile-system

## Raw Request

agent-profile-system : Tích hợp chữ ký mật mã Ed25519 ( IntegritySigner ) để xác minh tính toàn vẹn và bảo vệ Agent.

## Problem

Agents running within Aether lack a mechanism to guarantee the integrity of their self-models, memory, and code files. Additionally, the agent's life cycle (Init, Idle, Introspecting, Running, Sleeping) is not explicitly managed, making it difficult to coordinate operations such as daily dreaming/compaction or proactive background tasks.

## Desired Outcome

- Implement an `IntegritySigner` using Ed25519 cryptographic signatures to sign and verify files within the agent's workspace.
- Implement a `LifecycleStateMachine` that governs the active state of the agent.
- Fragment and optimize the `EpisodicLogger` to provide detailed history.

## Users / Actors

- The Aether Agent (Maria/Aura/Vesta) and the Aether host system.

## Current Context

Currently, files in the workspace can be modified arbitrarily without verification. The agent lifecycle is handled implicitly through thread state rather than an explicit state machine, which makes background tasks like dreaming prone to concurrency issues.

## Proposed Direction

- Build `IntegritySigner.cs` in `src/Aether/Agents/` using `System.Security.Cryptography`.
- Integrate verification checks on agent startup (within `BootLoader` or `AgentProfile`).
- Implement `LifecycleStateMachine.cs` to track and enforce agent lifecycle transitions.

## Scope

- Core C# runtime assemblies (`Aether` project).
- Workspace verification logic.

## Non-Goals

- Implementing full OS-level sandbox verification.
- Enforcing signatures on user-supplied input files.

## Constraints

- Signing and verification must have minimal latency overhead (under 50ms on startup).
- Must run cleanly on macOS and other target platforms.

## Success Criteria

- Successful validation of agent directory integrity on startup.
- Lifecycle states change deterministically and can be queried.
- All unit tests compile and pass.

## Risks

- Lost keys could lock the agent out of its workspace.
- Mitigation: Provide a recovery/key-regeneration command tool.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- None.

## Assumptions

- Ed25519 keys will be stored securely in the agent's user-protected configuration directory.

## Spec Seeds

- Signature file (`_INTEGRITY`) in the workspace root.
- Lifecycle state transitions hook.

## Intent Approval

Status: APPROVED
Approved by: Thoor
Date: 2026-06-13
