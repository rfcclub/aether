# Intent: system-prompt-injection

## Raw Request

system-prompt-injection : Tái cấu trúc System Prompt thành 8 lớp ( Identity > Constitution > Execution Bias > ... > Skill Context ) để triệt tiêu hiện tượng pha loãng danh tính (identity drift) của các Agent.

## Problem

Over long conversations, AI agents suffer from "identity drift" where their unique persona, rules, and core execution guidelines are diluted by subsequent conversation turns and tool feedbacks. The system prompt is currently injected as a flat block of text, which does not convey the strict hierarchy of rules or enforce proper embodiment of the agent's identity.

## Desired Outcome

- Reorganize the System Prompt into an explicit **8-layer structure**:
  1. `Identity` (AGENTS.md, SOUL.md, IDENTITY.md, USER.md)
  2. `Constitution` (Non-Negotiable Red Lines)
  3. `Execution Bias` (Tactical guidelines)
  4. `Memory` (Durable facts)
  5. `Working State` (Current tasks/goals)
  6. `Recent Memory` (Context history)
  7. `Group Context` (Channel/thread data)
  8. `Skill Context` (Loaded workflows/scripts)
- Prepend an "embodiment directive" instructing the model to internalize the identity rather than treat it as reference materials.
- Establish a clear priority rule: `Constitution > Persona > User request > Tool feedback`.

## Users / Actors

- The Aether Agents (Maria, Aura, Vesta) running inside the runtime.

## Current Context

System prompts are assembled from file reads in a flat order without a structured hierarchy, leading to cases where the model treats its own profile file as "reference material" (e.g., saying "According to my SOUL.md...") instead of acting directly as the persona.

## Proposed Direction

- Update the prompt building logic in `AetherSoul` to compile the 8 layers.
- Inject the priority chain and embodiment directive at the very top.
- Handle conflicts between the persona voice (SOUL.md) and technical execution bias.

## Scope

- Prompt builder module within the C# Core (`AetherSoul.cs` and related prompt construction logic).

## Non-Goals

- Changing the contents of the individual files (`SOUL.md`, `USER.md`).
- Modifying the underlying LLM provider APIs.

## Constraints

- Prompt size must remain within token limits (e.g., under 10k tokens).
- Must preserve the core persona voice and behavior of each agent.

## Success Criteria

- System prompt compiles exactly into 8 structured layers.
- The agent refers to itself as the persona in all response scenarios (e.g., "Em là Maria" instead of referencing configuration files).
- Unit tests verify prompt assembly order and layer presence.

## Risks

- Model behavior may change significantly if the prompt structure is updated.
- Mitigation: Verify using evaluation tests or mock prompts.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- None.

## Assumptions

- Structuring the prompt using markdown headers (`## Layer name`) helps the model recognize the priority and boundaries.

## Spec Seeds

- Section wrapping logic with custom headers for each layer.
- Embodiment self-audit instruction hook.

## Intent Approval

Status: APPROVED
Approved by: Thoor
Date: 2026-06-13
