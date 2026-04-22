# Aether Progress

> Last Updated: 2026-04-22

## Current Phase

Phase 1: Core Rewrite.

## Completed

### Track A Scaffold Foundation

Status: Completed

- Created the .NET 9 solution and app project structure.
- Added the architecture folders for channels, routing, data, agent, providers, memory, sessions, and scheduler.
- Added the initial generic host setup in `src/Aether/Program.cs`.
- Added `src/Aether/appsettings.json` with assistant, channel, LLM, sandbox, and scheduler sections.
- Added `src/Aether/Data/Schema.sql` with the Phase 1 tables from the architecture.
- Added a dependency-free smoke test project for scaffold verification.

### Data, Queue, And Router Foundation

Status: Completed

- Added `Data/AetherDb.cs` with SQLite connection management and idempotent schema migration.
- Added group route registration and lookup on top of the `groups` table.
- Added smoke coverage for migration idempotency and Phase 1 table creation.
- Added `Channels/InboundMessage.cs` and `Channels/IChannel.cs` as shared channel contracts.
- Added `Routing/IMessageQueue.cs` and `Routing/ChannelMessageQueue.cs` using `System.Threading.Channels`.
- Added `Routing/RoutedMessage.cs` and `Routing/MessageRouter.cs` for trigger normalization, group lookup, and enqueueing.
- Wired `AetherDb`, `IMessageQueue`, and `MessageRouter` into host DI.

### Track B Foundation

Status: Completed

- Added `Providers/ILLMProvider.cs` with request, message, and response contracts.
- Added `Providers/OpenRouterProvider.cs` with direct `HttpClient` chat-completions support.
- Added `Sessions/Session.cs` and `Sessions/SessionManager.cs` backed by SQLite `sessions` and `messages`.
- Added `Memory/IMemorySystem.cs` and `Memory/FileMemory.cs` for global and group `CLAUDE.md` loading.
- Added `Agent/IToolExecutor.cs` and `Agent/DisabledToolExecutor.cs` as the tool boundary before sandbox implementation.
- Added `Agent/AetherSoul.cs` for the minimal core loop: load memory, load history, call LLM, save user/assistant messages.
- Added host DI wiring for provider, session manager, file memory, disabled tool executor, and `AetherSoul`.
- Added local `--prompt` / `--group` harness for future no-Telegram testing with a configured LLM provider.
- Added smoke coverage for OpenRouter request/response shape, session persistence, file memory loading, and the AetherSoul loop.
- Fixed the local prompt harness so it bypasses generic host startup when running one-off prompts under Windows `dotnet.exe` from WSL.
- Added `--model`, `--database-path`, and `--api-key-file` prompt-harness overrides for reliable real-provider testing without exposing API keys in command lines.
- Verified a real OpenRouter call with `minimax/minimax-m2.5:free`.

### Tool Executor Safe-Read Slice

Status: Completed

- Added `Agent/ToolExecutor.cs` with `SandboxOptions` bound from the existing `sandbox` config.
- Replaced `DisabledToolExecutor` in host DI and the local prompt harness with `ToolExecutor`.
- Added safe managed built-in tools for `read`, `glob`, and `grep`.
- Added allowed-path validation for filesystem inspection tools.
- Kept `bash`, `write`, and `edit` explicitly disabled until the sandbox/mutating-tool slice is implemented.
- Added smoke coverage for allowed reads, rejected outside-path reads, recursive glob, recursive grep, disabled future tools, and unknown-tool errors.

### Tool Executor Bash Slice

Status: Completed

- Added sandboxed `bash` execution through `ToolExecutor`.
- Enforced allowed working directories.
- Added timeout handling and process-tree cleanup.
- Added output capture and truncation.
- Added smoke coverage for success, denied cwd, non-zero exit, timeout, and truncation.

### Tool Executor Mutating-Tool Slice

Status: Completed

- Added allowed-path `write` support.
- Added allowed-path text replacement `edit` support.
- Added smoke coverage for file creation, rejected outside writes, successful edits, and missing-text edit failures.

### Provider Tool-Call Contract Slice

Status: Completed

- Extended provider contracts with tool definitions, tool result messages, and assistant tool calls.
- Updated OpenRouter request serialization for OpenAI-compatible tools.
- Updated OpenRouter response parsing for assistant tool calls.
- Added smoke coverage for tool definitions, tool result messages, and parsed tool calls.

### AetherSoul Tool-Loop Slice

Status: Completed

- Added built-in tool definitions for `read`, `glob`, `grep`, `bash`, `write`, and `edit`.
- Taught `AetherSoul` to continue provider calls when assistant tool calls are returned.
- Executes requested tools through `IToolExecutor` and appends tool-result messages.
- Preserves assistant tool-call messages so OpenRouter receives a valid tool-result conversation shape.
- Added smoke coverage for tool execution, provider continuation, tool definitions, and tool-result messages.

## In Progress

No active implementation task is in progress.

## Next Steps

Track B next:

1. Add token-budgeted session history trimming before full compaction.
2. Persist or summarize tool turns in session history if real-provider use shows the next request needs more than final assistant text.
3. Add more direct OpenRouter real-provider smoke around tool calls with `--api-key-file`.
4. Harden sandbox backends beyond the current process-backed implementation.

Track A later:

1. Implement `Channels/Telegram/TelegramChannel.cs` using `Telegram.Bot`.
2. Add Telegram channel tests around inbound message normalization without hitting the network.
3. Add group route bootstrap/config loading so Telegram chat IDs can be registered into SQLite.

## Verification

- Completed: `'/mnt/c/Program Files/dotnet/dotnet.exe' run --project tests/Aether.Tests/Aether.Tests.csproj`
  - Result: `Aether Track B foundation smoke tests passed.`
- Completed: `'/mnt/c/Program Files/dotnet/dotnet.exe' build Aether.sln`
  - Result: Build succeeded with 0 warnings and 0 errors.
- Completed: `'/mnt/c/Program Files/dotnet/dotnet.exe' run --project src/Aether/Aether.csproj -- --smoke`
  - Result: Process exited with code 0.
- Completed: `dotnet run --project tests/Aether.Tests/Aether.Tests.csproj`
  - Result: `Aether Track B foundation smoke tests passed.`
- Completed: `dotnet build Aether.sln`
  - Result: Build succeeded with 0 warnings and 0 errors.
- Completed: `timeout 10s dotnet run --project src/Aether/Aether.csproj -- --smoke`
  - Result: Process exited with code 0.
- Completed: real OpenRouter prompt through built DLL using `--model minimax/minimax-m2.5:free`, Windows-local temp SQLite DB, and `--api-key-file`
  - Result: `Hello! Nice to meet you.`
