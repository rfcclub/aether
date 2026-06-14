## 1. Interfaces & contracts

- [x] 1.1 Create `ISlashCommandHandler` interface with `HandleAsync(SlashCommandContext, CancellationToken)` returning `SlashCommandResult?`
- [x] 1.2 Create `SlashCommandContext` record (Text, AgentName, WorkspacePath, Services)
- [x] 1.3 Create `SlashCommandResult` record (Text)

## 2. Tests — dispatcher logic

- [x] 2.1 Write test: non-slash message returns null (passthrough)
- [x] 2.2 Write test: unknown slash command returns null (passthrough)
- [x] 2.3 Write test: known slash command returns non-null result
- [x] 2.4 Write test: `/new` triggers session creation and context clear
- [x] 2.5 Write test: `/reset` clears context without creating new session
- [x] 2.6 Write test: `/model` with no args returns current model chain
- [x] 2.7 Write test: `/model <name>` updates ProviderRouter.ModelChain
- [x] 2.8 Write test: `/model <unknown>` warns but still sets
- [x] 2.9 Write test: `/context` returns session stats with message count
- [x] 2.10 Write test: `/compact` calls CompactContext and returns token estimate
- [x] 2.11 Write test: `/context` with no active session shows zero counts
- [x] 2.12 Write test: `/compact` with empty context shows minimal message

## 3. SlashCommandHandler implementation

- [x] 3.1 Implement `SlashCommandHandler : ISlashCommandHandler` with command dispatch table
- [x] 3.2 Implement `/new` handler — SessionManager.GetOrCreateSessionAsync + clear context
- [x] 3.3 Implement `/reset` handler — clear context, keep session
- [x] 3.4 Implement `/model` handler — show current or switch via ProviderRouter.ModelChain
- [x] 3.5 Implement `/context` handler — gather stats from SessionManager + IMemorySystem
- [x] 3.6 Implement `/compact` handler — call IMemorySystem.CompactContext(4000)

## 4. Integration — ChannelMessageProcessor

- [x] 4.1 Inject `ISlashCommandHandler` into `ChannelMessageProcessor`
- [x] 4.2 Call `HandleAsync` before the LLM scope; return direct response if handled
- [x] 4.3 Register `ISlashCommandHandler` → `SlashCommandHandler` as singleton in `Program.cs`

## 5. Integration tests

- [x] 5.1 Write end-to-end test: Telegram message "/new" returns session ID, no LLM call
- [x] 5.2 Write end-to-end test: Telegram message "hello" passes through to LLM
- [x] 5.3 Write end-to-end test: "/model claude-sonnet-4-6" switches model on next LLM call
- [x] 5.4 Run full test suite, verify no regressions

## 6. Cleanup

- [x] 6.1 Verify KuroClaw compatibility — interface has no Aether-specific types
- [x] 6.2 Run `dotnet test` — all tests green
