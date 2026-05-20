# Aether Progress

> Last Updated: 2026-04-27

## Current Phase

Phase 1: Core Rewrite — implementation complete. Phase 2: Hardening and channel integration — core hardening COMPLETE.

## Completed

### Maria Memory & Boundary Plugin

Status: Completed (Phase 2)

- **Phase 1 (Core)**: Native .NET 9 Plugin, JSONL Storage, Boundary Sanitizer, Daily Diary automation.
- **Auto-Promotion Engine**: Scoring logic for "lasting truths" based on significance, recall count, and user tags.
- **Smart Context Assembly**: Priority scoring (`recency * relevance * importance`) with a strict **7000 token budget**.
- **Dreaming System**: 3-phase background task (Light/REM/Deep) for score recalculation and pattern detection.
- **Python Extension Hook**: Minimal REST API on **port 5077** for external Python tool/script integration.
- **Research Integration**: SQLite-based graph linking between memory nodes and `research/` findings.
- **Slash Commands**: Added `/memory promote` and `/memory link-research <topic>`.

### Core Hardening (Vesta Forge)
- ✅ **Security**: Resolved NU1903 vulnerability in DBus. Protocol upgraded to 0.21.3.
- ✅ **Sandbox Safety**: Fixed critical default-allow bug. Verified path traversal and command filtering.
- ✅ **AetherSoul Loop**: Verified edge cases (empty prompt, max iteration limit).
- ✅ **Memory Integrity**: Verified dual-write (SQLite + Markdown). Implemented RAG-based context injection.
- ✅ **Reliability**: Implemented 10k token budgeting and exponential backoff retries.
- ✅ **Benchmark Gate**: Added detailed report parsing for self-improvement statistics.
- ✅ **Documentation**: Created `USER_GUIDE.md` and `CONFIGURATION.md`. Refined `README.md` for better UX.

### Provider System

Status: Completed

- Added `ILLMProvider.cs` — unified interface (chat, stream, tools)
- Added `OpenRouterProvider.cs`, `FireworksProvider.cs` — direct `HttpClient` implementations
- Added `AnthropicCompatibleProviderBase.cs`, `OpenAiCompatibleProviderBase.cs` — reusable base classes
- Added `GenericHttpProvider.cs` — generic HTTP provider with streaming support
- Added `ProviderRouter.cs` — multi-provider routing with fallback chain
- Added `ProviderHealthMonitor.cs` — health tracking and circuit breaker per provider

### Tool Executor (Sandboxed)

Status: Completed

- Added `Agent/ToolExecutor.cs` — sandboxed execution with allowed-path validation
- Added `Tooling/ToolExecutor.cs` — separate Tooling namespace implementation
- Added `ToolRegistry.cs` — dynamic tool registration
- Built-in tools: `read`, `glob`, `grep`, `bash`, `write`, `edit`
- Timeout handling, process-tree cleanup, output truncation
- Allowed-path validation for filesystem tools

### AetherSoul Tool Loop

Status: Completed

- Added `Agent/AetherSoul.cs` — minimal core loop: load memory → load history → call LLM → save messages
- Tool definitions for all built-in tools exposed to provider
- Continues provider calls when assistant tool calls returned
- Executes tools through `IToolExecutor`, appends tool-result messages
- Preserves assistant tool-call messages for valid tool-result conversation shape

### Memory System

Status: Completed (skeleton + full implementation)

- Added `IMemorySystem.cs` — interface specification
- Added `SqliteMemorySystem.cs` — full implementation with FTS5 search
- Added `FileMemory.cs` — global and group `CLAUDE.md` loading
- Three-layer design: Ephemeral, Working (FTS5), Durable
- Tracking tables for promotion candidates
- Bounds checking with force consolidation

### Skill System

Status: Completed

- Added `Skills/SkillInterfaces.cs` — ISkillRegistry, ISkillLoader, ISkillTrigger, SkillDefinition, SkillContext
- Added `Skills/SkillParser.cs` — SKILL.md parser (frontmatter YAML + body)
- Added `Skills/SkillRegistry.cs` — skill registration with keyword-match auto-detection
- Added `Skills/SkillTrigger.cs` — explicit (/skill-name) and auto (keyword overlap) trigger detection
- Added `Skills/SkillEvolution.cs` — usage tracking, recidivism detection, PromotionCandidate output
- Integrated skill context injection into AetherSoul BuildSystemPrompt
- Skill system wired into DI in Program.cs

### Session Manager

Status: Completed

- Added `Sessions/Session.cs` and `Sessions/SessionManager.cs`
- SQLite-backed `sessions` and `messages` tables
- Session history persistence and loading

### Data Layer

Status: Completed

- Added `Data/AetherDb.cs` — SQLite connection management, idempotent schema migration
- Added FTS5 virtual table for working memory search
- Tracking tables: `sessions`, `messages`, `promotion_candidates`
- `AetherInitializationService.cs` — host initialization

### Provider Tool-Call Contract

Status: Completed

- Extended provider contracts with tool definitions, tool result messages, assistant tool calls
- OpenRouter request serialization for OpenAI-compatible tools
- Response parsing for assistant tool calls

### Telegram Channel

Status: Completed

- Added `Channels/TelegramChannel.cs` — IChannel implementation using Telegram.Bot
- Message polling loop with auto-reconnect
- Converts Telegram messages to InboundMessage, fires OnMessage event
- Supports SendMessageAsync, SetTypingAsync, OwnsChatId
- Added `Channels/NoOpChannel.cs` — no-op channel for when Telegram is disabled
- Added `Channels/ChannelMessageProcessor.cs` — BackgroundService that bridges channel → router → AetherSoul → channel

### Test Suite

Status: Completed

- Converted test project to xUnit with 13 test classes
- Tests: LlmMessage, ChannelMessageQueue, SkillParser, SkillRegistry, SkillTrigger, SkillEvolution, ProviderRouter, SessionManager, AetherDb, FileMemory, OpenRouterProvider, AetherSoul, MessageRouter, ToolRegistry
- Original smoke test preserved in Program.cs

## Verification

- 21 commits from baseline (commit `ea20857` through `02e100d`)
- Latest commit: `ea20857 feat: add provider base classes and generic HTTP provider`
- Working tree clean on `master` branch
- OpenSpec specs archived for all 8 changes:
  - `2026-04-27-aether-full-project` (+33 requirements)
  - `2026-04-27-provider-base-classes` (+9)
  - `2026-04-27-llm-provider-extensions` (+9)
  - `2026-04-27-llm-tool-call-loop` (+8)
  - `2026-04-27-tool-executor-sandbox` (+5)
  - `2026-04-27-session-history-trimming` (+4)
  - `2026-04-27-telegram-channel` (+7)
  - `2026-04-27-skill-system` (+11)
- Total specs added: +86 requirements across 22 spec files

## Notes

- `PLAN.md` references old team names (Miriam/Erza/2B) — plan is historical
- `OPEN_SPEC.md` reflects current architecture accurately
- Track A (infrastructure/channel) and Track B (agent/provider) completed as parallel implementation

### JSON Schema Validation

Status: Completed

- Added `NJsonSchema` v11.0.0 package
- Added `SchemaJson` property to `LlmTool` record
- Created `Tooling/ParameterValidator.cs` — static validator with compiled schema cache
- Added JSON Schema for all 6 built-in tools with `additionalProperties: false`
- Wired validation into `AetherSoul.RunLlmToolLoopAsync` — validates before execution
- Validation errors formatted with path + kind, returned as tool result for LLM self-correction
- 11 tests in `ParameterValidatorTests.cs`

### AIAgent Core Refinements

Status: Completed

- **Token-aware truncation**: `MaxContextTokens` (120k), char/4 estimation, removes oldest history first
- **Streaming support**: `CompleteStreamingAsync` added to `ILLMProvider` interface, implemented in all providers, `ProcessStreamingAsync` on `AetherSoul`
- **Session improvements**: `GetRecentSessionsAsync` on `ISessionManager`/`SessionManager`, ordered by last activity

### TUI Project

Status: Completed

- Added `src/Aether.Tui/` — Terminal.Gui v1.19.0 console app
- Full chat UI: group list, chat history, input bar, Send button
- DI container with same service configuration as main Program.cs
- Thread-safe chat updates via `Application.MainLoop.Invoke()`
- Keyboard: Enter sends, Ctrl+Q quits
- 3 projects build: Aether, Aether.Tui, Aether.Tests

### Self-Improvement Workflow

Status: Completed

- Added `SelfImprovement/` directory with 6 new files
- `CandidateState.cs` — PROPOSED/APPLIED/VERIFIED/FAILED enum
- `DailyReviewHostedService.cs` — BackgroundService with midnight UTC polling
- `SelfImprovementService.cs` — 5-phase pipeline: reflection, promotion, recidivism, benchmark, surfacing
- `BenchmarkGate.cs` — process-based `dotnet test` runner with configurable timeout
- `PipelineTracker.cs` — SQLite-backed candidate state persistence via AetherDb
- `IPipelineTracker.cs`, `IBenchmarkGate.cs`, `ISelfImprovementService.cs` — interfaces
- Added `pipeline_states` table to Schema.sql with UNIQUE candidate_hash
- Added `GetRecentSessionsAsync` to `IMemorySystem` and both implementations
- Added `GeneratePatchAsync` to `ISkillEvolution` with structured markdown patch output
- Wired all 5 self-improvement services into DI in Program.cs
- Created `patches/` directory with `.gitkeep`
- 16 new tests across 4 test files (PipelineTracker, BenchmarkGate, SelfImprovementService, Memory)
- 112 total tests pass, 0 fail

### Streaming Support

Status: Completed

- Added `SendStreamingChunkAsync` and `SendStreamingCompleteAsync` to `IChannel` (default no-op for backward compat)
- Added `StreamEvent` discriminated union (TextToken | Response) and `CompleteStreamingEventsAsync` to `ILLMProvider`
- `TelegramChannel` streaming: first chunk sends new message, subsequent chunks edit in-place via `EditMessageText`
- `ChannelMessageProcessor` switched from `ProcessAsync` to `ProcessStreamingAsync`, capped at 50 streaming edits
- `AetherSoul.ProcessStreamingAsync`: full tool-call loop with streaming, token buffering outside try-catch, tool validation + execution in stream
- `OpenAiCompatibleProviderBase`: real SSE parsing with `ResponseHeadersRead`, `data: [...]` line parsing, tool-call delta accumulation
- `AnthropicCompatibleProviderBase`: real Anthropic SSE format (content_block_start/delta/stop, message_start/stop), text_delta + input_json_delta
- `ProviderRouter`: streaming passthrough with fallback, event buffering for yield-in-try-catch workaround
- 4 new streaming tests in AetherSoulTests.cs

### WebSocket Channel

Status: Completed

- Added `WebSocketChannel.cs` — IChannel implementation with HttpListener + WebSocket upgrade on `/ws`
- Added `WebSocketChannelService.cs` — BackgroundService for listener lifecycle
- JSON protocol: `{"type":"message","text":"..."}`, typing indicators, error messages
- Configuration: `channels:websocket:port` (default 5099), `channels:websocket:enabled`
- Multi-client support with connection tracking, cleanup on disconnect
- Wired into DI in Program.cs alongside Telegram channel

### TUI Enhancements

Status: Completed

- F5 group cycling through available groups, group name in status bar
- PgUp/PgDn scrollback with line indicator (L1-20/120), auto-scroll toggle
- Ctrl+W word wrap toggle with status bar indicator
- Status bar: Group | Wrap | Scroll | Quit | Switch | Wrap Toggle
- Build fixes: yield/try/catch in AetherSoul, variable shadowing in OpenAiCompatibleProviderBase

### Tool Hot-Reload

Status: Completed

- Added `ToolHotReloadService.cs` — BackgroundService with FileSystemWatcher on `tools/` directory
- 2-second debounce timer prevents duplicate registration from rapid file events
- Periodic sweep timer (5s) catches deletions missed by FileSystemWatcher on WSL/network shares
- Parses `.json` tool definitions and registers hot-reloaded tools at runtime
- Invalid JSON logged and skipped — no crash, existing tools unaffected
- Configurable `tools/` path via `tooling:hot_reload_path` in appsettings.json (default: `tools`)
- `tools/.gitkeep` placeholder file for git tracking
- 9 new tests in `ToolHotReloadServiceTests.cs` — all pass

## In Progress

No active implementation task.

## Next Steps

### High Priority

1. ~~**Skill System**~~ ✅ Done
2. ~~**Gateway / Telegram**~~ ✅ Done
3. ~~**JSON Schema validation**~~ ✅ Done

### Medium Priority

4. ~~**AIAgent Core**~~ ✅ Done
5. ~~**Self-Improvement Workflow**~~ ✅ Done
6. ~~**Streaming support**~~ ✅ Done
7. ~~**WebSocket channel**~~ ✅ Done
8. ~~**TUI enhancements**~~ ✅ Done

### Lower Priority

9. **Multi-Agent** — decision pending (profile isolation vs gateway routing)
10. ~~**Hot-reload**~~ ✅ Done — FileSystemWatcher for tool definitions, 2s debounce, sweep fallback