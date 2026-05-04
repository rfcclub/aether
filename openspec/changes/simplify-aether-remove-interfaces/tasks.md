## 1. Remove single-implementation interfaces

- [ ] 1.1 Remove `src/Aether/Agents/IBootContract.cs` — only `BootContract` implements it
- [ ] 1.2 Remove `src/Aether/Agents/IAgentProfile.cs` — only `AgentProfile` implements it; merge interface doc comments into concrete class
- [ ] 1.3 Remove `src/Aether/Memory/IMemorySystem.cs` — only `FileMemory` implements it
- [ ] 1.4 Remove `src/Aether/Sessions/ISessionManager.cs` — only `SessionManager` implements it
- [ ] 1.5 Remove `src/Aether/Agent/IToolExecutor.cs` — only `ToolExecutor` implements it
- [ ] 1.6 Remove `src/Aether/Tooling/IToolRegistry.cs` — only `ToolRegistry` implements it
- [ ] 1.7 Remove `src/Aether/Skills/ISkillRegistry.cs` — only `SkillRegistry` implements it
- [ ] 1.8 Remove `src/Aether/Skills/ISkillLoader.cs` — only `SkillParser` implements it
- [ ] 1.9 Remove `src/Aether/Skills/ISkillTrigger.cs` — only `SkillTrigger` implements it
- [ ] 1.10 Remove `src/Aether/Skills/ISkillEvolution.cs` — only `SkillEvolution` implements it
- [ ] 1.11 Remove `src/Aether/SelfImprovement/IPipelineTracker.cs` — only `PipelineTracker` implements it
- [ ] 1.12 Remove `src/Aether/SelfImprovement/IBenchmarkGate.cs` — only `BenchmarkGate` implements it
- [ ] 1.13 Remove `src/Aether/SelfImprovement/ISelfImprovementService.cs` — only `SelfImprovementService` implements it

## 2. Make concrete classes directly usable

- [ ] 2.1 Make key methods `virtual` on `FileMemory` (LoadContextAsync, AppendAsync) so tests can subclass
- [ ] 2.2 Make key methods `virtual` on `SessionManager` (GetOrCreateSessionAsync, GetHistoryAsync, AppendMessageAsync)
- [ ] 2.3 Make key methods `virtual` on `ToolExecutor` (ExecuteAsync)
- [ ] 2.4 Make key methods `virtual` on `SkillRegistry` (List, Register)
- [ ] 2.5 Make key methods `virtual` on `SkillTrigger` (DetectTrigger)
- [ ] 2.6 Make key methods `virtual` on `AgentProfile` (LoadPersonaAsync, LoadFileAsync, LoadDailyMemoryAsync)

## 3. Simplify BootContract to static BootLoader

- [ ] 3.1 Create `src/Aether/Agents/BootLoader.cs` with `static Task<string> LoadFilesAsync(string agentDir, IReadOnlyList<string> paths, CancellationToken ct)`
- [ ] 3.2 Delete `src/Aether/Agents/BootContract.cs` — replaced by `BootLoader`
- [ ] 3.3 Update `AetherSoul` to call `BootLoader.LoadFilesAsync()` instead of `_bootContract.LoadConstitutionAsync()` etc.
- [ ] 3.4 Remove `IBootContract?` parameter from `AetherSoul` constructor

## 4. Remove WriteValidator and BootLayer

- [ ] 4.1 Delete `src/Aether/Agents/WriteValidator.cs`
- [ ] 4.2 Remove `BootLayer` enum (in WriteValidator.cs, deleted with it)
- [ ] 4.3 Remove `WriteValidator` DI registration from `Program.cs`

## 5. Update DI registrations in Program.cs

- [ ] 5.1 Replace all `AddSingleton<I*, Impl>()` with `AddSingleton<Impl>()` for removed interfaces
- [ ] 5.2 Update all constructor parameters and field types to use concrete types
- [ ] 5.3 Remove `using` directives for deleted interface namespaces

## 6. Update DI registrations in App.axaml.cs

- [ ] 6.1 Replace interface-based registrations with concrete type registrations
- [ ] 6.2 Update `AetherSoul` construction to not require `IBootContract`
- [ ] 6.3 Build and verify zero errors

## 7. Update tests

- [ ] 7.1 Convert `FakeMemorySystem` to extend `FileMemory` and override virtual methods
- [ ] 7.2 Convert `FakeSessionManager` to extend `SessionManager` and override virtual methods
- [ ] 7.3 Convert `FakeToolExecutor` to extend `ToolExecutor` and override virtual methods
- [ ] 7.4 Update `AgentIntegrationTests` to use concrete types
- [ ] 7.5 Update `ProviderRouterTests` (if any) to use concrete types
- [ ] 7.6 Remove test files that tested removed classes (WriteValidatorTests.cs, LifecycleStateMachineTests.cs, EpisodicLoggerTests.cs if EpisodicLogger removed)

## 8. Build and verify

- [ ] 8.1 `dotnet build` — zero errors
- [ ] 8.2 `dotnet test` — all tests pass
- [ ] 8.3 Verify `aether agent add` CLI commands still work
- [ ] 8.4 Verify Telegram channel still routes messages correctly
