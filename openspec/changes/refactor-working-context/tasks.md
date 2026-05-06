# Tasks: Refactor Working Context

## Track 1 — WorkingContext core

- [x] 1.1 Create `WorkingContext` class (system prompt + messages + tools)
- [x] 1.2 Integrate WorkingContext into AetherSoul.ProcessAsync
- [x] 1.3 Integrate WorkingContext into AetherSoul.ProcessStreamingAsync
- [x] 1.4 Remove per-turn context loading from AetherSoul (LoadDailyMemory, etc.)

## Track 2 — De-async codebase

- [x] 2.1 ToolExecutor: file ops to sync (Read, Write, Edit, Grep, Glob)
- [x] 2.2 FileMemory: add sync LoadContext
- [x] 2.3 AgentProfile: add sync LoadFile, LoadDailyMemory
- [x] 2.4 BootContract: add sync LoadWorkingState
- [x] 2.5 SessionManager: add in-memory sync API (GetOrCreateSession, AppendMessage, GetHistory)

## Track 3 — Remove deprecated complexity

- [x] 3.1 ContextAssembler used once at startup via AgentProfile.LoadIdentityContext (not per-turn)
- [x] 3.2 Remove 7-layer system prompt from AetherSoul.BuildSystemPrompt
- [x] 3.3 SlashCommandHandler already clean — no identity cache invalidation found
- [x] 3.4 Remove unused constructor dependencies from AetherSoul (memory, sessions, skills, skillTrigger, bootContract, contextAssembler)

## Track 4 — Tests

- [x] 4.1 Update test doubles for new sync API
- [x] 4.2 All 338 tests pass (excl. pre-existing Tavily network failure)
- [x] 4.3 Add WorkingContext unit tests (11 tests, all pass)
