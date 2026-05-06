## 1. Context Assembler Service

- [x] 1.1 Create `ContextAssembler.cs` in `src/Aether/Agent/` with `AssembleIdentityContextAsync(agentDir)` method
- [x] 1.2 Implement dynamic `.md` file discovery with fixed priority order (SOUL.md > IDENTITY.md > USER.md > AGENTS.md > AGENTS_GUARD.md > MEMORY.md > alphabetical)
- [x] 1.3 Implement `AssembleDynamicContextAsync(agentDir, sessionId)` for working state, recent memory, group context
- [x] 1.4 Exclude dynamic files (TASK_INBOX.md, HEARTBEAT.md, `memory/*.md`) from identity context; include in dynamic context
- [x] 1.5 Add configurable subdirectory recursion flag (default: disabled / flat discovery)
- [x] 1.6 Add token budget enforcement for dynamic context section
- [x] 1.7 Register `ContextAssembler` in DI container in `Program.cs`

## 2. Refactor BuildSystemPrompt

- [x] 2.1 Rewrite `BuildSystemPrompt()` signature to 3 parameters: `identityContext`, `dynamicContext`, `skillContext?`
- [x] 2.2 Remove all 8-layer code: Identity preamble, Constitution, Conflict Resolution, ritual markers
- [x] 2.3 Build Identity Context section: minimal heading + assembled identity file content
- [x] 2.4 Build Behavior Instructions section: safety gate + execution bias + code style + verification + anti-hallucination + tone
- [x] 2.5 Add `SYSTEM_PROMPT_CACHE_BOUNDARY` marker between Behavior Instructions and Dynamic Context
- [x] 2.6 Build Dynamic Context section from dynamic context parameter + skill context
- [x] 2.7 Add "Current Date" to Identity Context section
- [x] 2.8 Verify output: no "Constitution", "2B", "ritual", "ALREADY DONE", "You ARE this agent" strings

## 3. Update Callers of BuildSystemPrompt

- [x] 3.1 Update `RunLlmToolLoopAsync()` to use `ContextAssembler` + new `BuildSystemPrompt()` signature
- [x] 3.2 Update `ProcessStreamingAsync()` to use new signature
- [x] 3.3 Update `ProcessTaskAsync()` to use new signature
- [x] 3.4 Verify all three entry points compile and pass existing tests

## 4. Deprecate BootConfig Semantic Slots

- [x] 4.1 Mark `BootConfig.ConstitutionFiles`, `IdentityFiles`, `CognitiveFiles` as `[Obsolete]` with deprecation message
- [x] 4.2 When deprecated fields have values, merge them into discovered files list (no semantic labeling)
- [x] 4.3 Log deprecation warning when deprecated fields are in use
- [x] 4.4 Update `AgentConfig.cs` defaults to remove hardcoded file lists (use empty lists with deprecation note)

## 5. Update AgentProfile and BootContract

- [x] 5.1 Add `LoadIdentityContextAsync()` method to `AgentProfile` delegating to `ContextAssembler`
- [x] 5.2 Deprecate `LoadPersonaAsync()`, `LoadConstitutionAsync()`, `LoadIdentityAsync()`, `LoadCognitiveAsync()` individual methods
- [x] 5.3 Keep existing methods as backward-compatible wrappers that delegate to `ContextAssembler`
- [x] 5.4 Update `BootContract.LoadFilesAsync()` to use discovery instead of hardcoded path list

## 6. Tests

- [x] 6.1 Add test: `BuildSystemPrompt` output contains exactly 3 sections
- [x] 6.2 Add test: output contains cache boundary marker
- [x] 6.3 Add test: output does NOT contain "Constitution > Persona", "You ARE this agent", "ritual", "2B", "ALREADY DONE"
- [x] 6.4 Add test: safety gate categories present in output
- [x] 6.5 Add test: execution bias directives preserved
- [x] 6.6 Add test: anti-hallucination directive preserved
- [x] 6.7 Add test: dynamic file discovery excludes TASK_INBOX.md from identity context
- [x] 6.8 Add test: file loading respects priority order
- [x] 6.9 Run full test suite: `dotnet test`
- [x] 6.10 Verify existing tool loop tests still pass (no regression)
