## Why

Aether inherited Java-style `I*` interface-for-everything patterns: `IBootContract`, `IMemorySystem`, `ISessionManager`, `IToolRegistry`, `IToolExecutor`, `ISkillRegistry`, `ISkillLoader`, `ISkillTrigger`, `ISkillEvolution`, `IPipelineTracker`, `IBenchmarkGate`, `ISelfImprovementService`, `IAgentProfile`. Each has exactly one production implementation. These interfaces add indirection without abstraction value — debugging requires "go to definition" twice, DI registrations are boilerplate, and the cognitive load of tracking interface→impl mappings is pure overhead. Interfaces that exist for testing can be replaced by virtual methods on concrete classes.

## What Changes

- **Remove 12 single-implementation interfaces**: `IBootContract`, `IAgentProfile`, `IMemorySystem`, `ISessionManager`, `IToolExecutor`, `IToolRegistry`, `ISkillRegistry`, `ISkillLoader`, `ISkillTrigger`, `ISkillEvolution`, `IPipelineTracker`, `IBenchmarkGate`, `ISelfImprovementService`. Register concrete types directly in DI. **BREAKING** for any external code referencing these interfaces.
- **Keep multi-implementation interfaces**: `ILLMProvider` (5+ providers), `IChannel` (Telegram, WebSocket, Console, NoOp). These justify abstraction.
- **Simplify BootContract**: `BootContract` class becomes a static method or simple function — it's just `LoadFilesAsync(paths)`. `BootConfig` stays as config record. **BREAKING**: removes `BootLayer` enum and `WriteValidator` class (unused in current code paths).
- **Update all DI registrations**: Replace `AddSingleton<IFoo, Foo>()` with `AddSingleton<Foo>()`.
- **Update tests**: Replace interface-based fake implementations with concrete class subclasses or direct instantiation.
- **Update Program.cs and App.axaml.cs**: Direct DI, no interface proxies.

## Capabilities

### Modified Capabilities
- *(none — no existing specs to modify; this is a code structure change with no behavioral impact)*

## Impact

- **`src/Aether/Agents/`**: Remove `IBootContract.cs`, `IAgentProfile.cs` (merged into concrete), `WriteValidator.cs`, `BootContract.cs` (simplified). Keep `AgentProfile.cs`, `AgentConfig`, `BootConfig`.
- **`src/Aether/Memory/`**: Remove `IMemorySystem.cs`, register `FileMemory` directly.
- **`src/Aether/Sessions/`**: Remove `ISessionManager.cs`, register `SessionManager` directly.
- **`src/Aether/Agent/`**: Remove `IToolExecutor.cs` (interface), keep `ToolExecutor.cs`.
- **`src/Aether/Tooling/`**: Remove `IToolRegistry.cs`, register `ToolRegistry` directly.
- **`src/Aether/Skills/`**: Remove `ISkillRegistry.cs`, `ISkillLoader.cs`, `ISkillTrigger.cs`, `ISkillEvolution.cs`.
- **`src/Aether/SelfImprovement/`**: Remove `IPipelineTracker.cs`, `IBenchmarkGate.cs`, `ISelfImprovementService.cs`.
- **`src/Aether/Program.cs`**, **`src/Aether.Terminal/App.axaml.cs`**: Update all DI and usings.
- **`tests/Aether.Tests/`**: Update all tests to use concrete classes.
