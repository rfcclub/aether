# Intent: simplify-aether-remove-interfaces

## Raw Request

simplify-aether-remove-interfaces : Loại bỏ các Interface dư thừa chỉ có 1 class triển khai (như IAgentProfile , IMemorySystem ), chuyển sang dùng class cụ thể đánh dấu virtual để tăng tốc độ phát triển và làm sạch code.

## Problem

Aether inherited Java-style `I*` interface-for-everything patterns (e.g., `IBootContract`, `IAgentProfile`, `IMemorySystem`, `ISessionManager`, `IToolExecutor`, `IToolRegistry`, `ISkillRegistry`, `ISkillLoader`, `ISkillTrigger`, `ISkillEvolution`, `IPipelineTracker`, `IBenchmarkGate`, `ISelfImprovementService`). Each of these has exactly one production implementation. These interfaces add indirection without abstraction value. Debugging requires navigating through interfaces, DI registrations are unnecessarily boilerplate, and the cognitive overhead of tracking interface-to-implementation mappings is high.

## Desired Outcome

- Remove all 12 single-implementation interfaces.
- Register concrete types directly in Dependency Injection (DI) containers.
- Mark concrete class methods as `virtual` to support subclassing and mocking in tests.
- Keep only interfaces that have multiple implementations (e.g., `ILLMProvider` and `IChannel`).

## Users / Actors

- Developers maintaining the Aether codebase.

## Current Context

Currently, services like `FileMemory` implement `IMemorySystem`, `SessionManager` implements `ISessionManager`, and `ToolExecutor` implements `IToolExecutor`. All DI registrations map these interfaces to their concrete implementations.

## Proposed Direction

- Remove files: `IBootContract.cs`, `IAgentProfile.cs`, `IMemorySystem.cs`, `ISessionManager.cs`, `IToolExecutor.cs`, `IToolRegistry.cs`, `ISkillRegistry.cs`, `ISkillLoader.cs`, `ISkillTrigger.cs`, `ISkillEvolution.cs`, `IPipelineTracker.cs`, `IBenchmarkGate.cs`, `ISelfImprovementService.cs`.
- Update classes to use `virtual` modifier on public methods.
- Update DI registrations in `Program.cs` and `App.axaml.cs`.
- Update tests to use direct instantiation or mock subclasses.

## Scope

- Core C# runtime assemblies (`Aether` project).
- Test assemblies (`Aether.Tests` project).
- UI client assembly (`Aether.Tui` project).

## Non-Goals

- Removing multi-implementation interfaces (`ILLMProvider`, `IChannel`).
- Rewriting core logic of the services.

## Constraints

- Backward compatibility for existing DI structure where possible, but this change is inherently breaking for internal dependencies.
- Ensure all 450+ unit tests continue to pass.

## Success Criteria

- 12 interfaces removed completely from the repository.
- DI container registers concrete classes directly.
- 100% of unit tests compile and pass.

## Risks

- Breaking third-party plugins or external code referencing the interfaces.
  - Mitigation: Ensure plugins are updated to reference concrete classes directly.

## Ambiguities

### Blocking

- None.

### Non-Blocking

- None.

## Assumptions

- Replacing interfaces with virtual concrete methods is sufficient for unit test mocking in C# using standard frameworks (Moq).

## Spec Seeds

- Direct DI resolution of `FileMemory`, `SessionManager`, `ToolExecutor`, etc.
- Removal of `WriteValidator` and `BootLayer` (unused code).

## Intent Approval

Status: APPROVED
Approved by: Thoor
Date: 2026-06-13
