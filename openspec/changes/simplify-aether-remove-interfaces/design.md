## Context

Aether codebase uses interface-per-class pattern imported from Java: every concrete type gets an `I*` interface. Currently 13 interfaces map 1:1 to implementations. These exist for two stated reasons: (a) DI/IoC pattern, (b) test mocking. Both are solvable without interfaces.

.NET's DI container supports concrete type registration directly. For testing, C# supports virtual methods — fake implementations can override behavior without an interface.

## Goals / Non-Goals

**Goals:**
- Remove 12 single-implementation interfaces, register concrete types in DI
- Simplify `BootContract` (file loader) into a static method — it's 20 lines of file I/O
- Remove unused `WriteValidator` and `BootLayer` enum
- Make code navigable with single "go to definition" instead of two jumps
- Reduce boilerplate DI registrations
- Reduce files in the Agents/ directory from 12 to ~6

**Non-Goals:**
- Keep `ILLMProvider` (5+ implementations justify abstraction)
- Keep `IChannel` (4 implementations justify abstraction)
- No behavioral changes to agent runtime
- No config format changes

## Decisions

### Decision 1: Virtual methods over interfaces for test faking

**Chosen**: Make key methods `virtual` on concrete classes where tests need custom behavior. Keep existing `FakeLlmProvider`, `FakeMemorySystem`, `FakeSessionManager`, `FakeToolExecutor` as subclasses of the concrete types.

**Alternative considered**: Keep interfaces only for testable types. Rejected — adds inconsistency (some types have interfaces, some don't) and doesn't reduce file count.

### Decision 2: BootContract → static method

**Chosen**: Replace `BootContract` class with `static Task<string> BootLoader.LoadFilesAsync(string agentDir, IReadOnlyList<string> paths, CancellationToken ct)`. The class had no state beyond what `BootConfig` already provides.

**Alternative considered**: Keep BootContract but remove interface. Rejected — a 20-line file reader doesn't need a class, DI registration, or constructor.

### Decision 3: Keep BootConfig record

**Chosen**: `BootConfig` stays as a POCO config record. It's a data object, not behavior — removing it gains nothing and breaks config deserialization.

### Decision 4: Remove WriteValidator and BootLayer

**Chosen**: These are not used in any active code path. `WriteValidator` was part of the old FEOFALLS write-boundary enforcement that Maria no longer uses.

### Decision 5: Concrete type DI registration

**Chosen**: `services.AddSingleton<FileMemory>()` then resolve `FileMemory` directly. No interface proxy overhead, simpler stack traces.

## Risks / Trade-offs

- **Test refactoring**: Tests that use interface-based fakes need updating. Risk is moderate — most fake classes can simply extend the concrete class. → Mitigation: refactor tests in same commit, run full suite.
- **Tight coupling**: Without interfaces, swapping implementations requires changing all references. → Acceptable: these types have never been swapped and show no sign of needing alternates; `ILLMProvider` and `IChannel` (the ones that DO vary) retain interfaces.
- **Merge conflicts**: Touching 20+ files. → Mitigation: do this as a single focused commit, merge when no other major branches are in flight.
