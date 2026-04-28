## 1. Foundation & DI Fixes

- [x] 1.1 Define `ILLMProvider` interface with `CompleteAsync(LlmRequest, ct)`
- [x] 1.2 Define `IMemorySystem` with 3-layer design (ephemeral/working/durable)
- [x] 1.3 Define `IToolExecutor` and `IToolRegistry` in `Tooling/`
- [x] 1.4 Define `IChannel` and `InboundMessage` channel abstractions
- [x] 1.5 Define `ISessionManager` and `Session` record
- [x] 1.6 Define `IMessageQueue` and `MessageRouter`
- [ ] 1.7 Remove duplicate `IToolExecutor` registration in `Program.cs`
- [ ] 1.8 Add `AetherInitializationService : IHostedService` that calls `AetherDb.InitializeAsync()` then `SqliteMemorySystem.InitializeAsync()` at startup
- [ ] 1.9 Add `NJsonSchema` package reference to `Aether.csproj`
- [ ] 1.10 Add `System.Net.WebSockets` / ASP.NET Core WebSocket middleware package if needed

## 2. Data Layer

- [x] 2.1 Create `AetherDb` with connection pooling and `InitializeAsync()`
- [x] 2.2 Create `Schema.sql` with `sessions` and `messages` tables
- [ ] 2.3 Add `messages_fts` FTS5 virtual table to `Schema.sql` (`CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(content, content=messages, content_rowid=rowid)`)
- [ ] 2.4 Add `AFTER INSERT ON messages` trigger in `Schema.sql` to sync FTS5
- [ ] 2.5 Add `promotion_candidates` table to `Schema.sql` (id, group_folder, text, confidence, evidence_count, status, created_at)
- [ ] 2.6 Add `provider_usage` table to `Schema.sql` (id, provider, model, input_tokens, output_tokens, cost_usd, timestamp)

## 3. Memory System

- [x] 3.1 Skeleton `SqliteMemorySystem` with `NotImplementedException` stubs
- [x] 3.2 `FileMemory` reads/writes `MEMORY.md` durable layer
- [ ] 3.3 Implement `SqliteMemorySystem.InitializeAsync()` — open connection, run schema SQL
- [ ] 3.4 Implement `LoadContextAsync()` — concatenate durable (MEMORY.md) + top-K working memory results
- [ ] 3.5 Implement `StoreMessageAsync()` — insert into `messages` table (FTS5 trigger handles sync)
- [ ] 3.6 Implement `SearchAsync()` — FTS5 query with `ORDER BY bm25(messages_fts)` and top-K limit
- [ ] 3.7 Implement priority-based ephemeral eviction (importance score: `tool_result=3 > assistant=2 > user=1`; FIFO tiebreak)
- [ ] 3.8 Implement `TryPromoteAsync()` — check confidence ≥ 0.7 and evidence_count ≥ 3, append to MEMORY.md
- [ ] 3.9 Implement `ForceConsolidationAsync()` — merge/evict lowest-confidence entries when MEMORY.md exceeds 2,500 chars
- [ ] 3.10 Write unit tests for promotion pipeline and eviction logic

## 4. LLM Provider Router

- [x] 4.1 Skeleton `ProviderRouter` with provider group fallback chain
- [x] 4.2 `OpenRouterProvider` implementation (streaming partial)
- [ ] 4.3 Rename `ProviderRouter.ChatAsync` → `CompleteAsync` to match `ILLMProvider` contract; update all call sites
- [ ] 4.4 Implement complexity scoring: keyword detection + message length heuristic → 0.0–1.0 score
- [ ] 4.5 Implement circuit breaker per endpoint: open after 3 failures, reset after 60s
- [ ] 4.6 Implement streaming via `IAsyncEnumerable<string>` through router (delegate to provider if `SupportsStreaming`)
- [ ] 4.7 Implement cost tracking: insert into `provider_usage` table after each successful call
- [ ] 4.8 Add `FireworksProvider` — Fireworks AI OpenAI-compatible endpoint (unlimited tier)
- [ ] 4.9 Wire `ProviderRouter` in `Program.cs` with Fireworks (primary) + OpenRouter (fallback) providers

## 5. Tool System

- [x] 5.1 Skeleton `ToolRegistry` with register/unregister/resolve/list
- [x] 5.2 Skeleton `ToolExecutor` in `Tooling/` namespace
- [x] 5.3 Built-in tool implementations (read, write, edit, bash, glob, grep) in `Agent/ToolExecutor.cs`
- [ ] 5.4 Implement NJsonSchema validation in `Tooling/ToolExecutor.ExecuteAsync()` — parse `ToolDefinition.ParametersJson`, validate args, return `ToolResult.Failure` on violation
- [ ] 5.5 Implement permission model — `SessionAllowlist` passed to `ExecuteAsync`; reject tools not on list
- [ ] 5.6 Implement `FileSystemWatcher` hot-reload in `ToolRegistry` — watch `tools/` directory, load/unload `.json` definition files
- [ ] 5.7 Register all 6 built-in tools at startup in `Program.cs`
- [ ] 5.8 Wire `Tooling/ToolExecutor` (with schema validation) as the primary `IToolExecutor` in DI

## 6. Agent Core

- [x] 6.1 `AetherSoul` with `ProcessAsync` and `RunLlmToolLoopAsync`
- [x] 6.2 Built-in tool definitions (LlmTool records) declared in `AetherSoul`
- [x] 6.3 Session persistence via `ISessionManager.AppendMessageAsync`
- [ ] 6.4 Make `MaxToolIterations` read from `agent:max_tool_iterations` config (default 8)
- [ ] 6.5 Replace `throw InvalidOperationException` on max iterations with graceful `AgentResponse` error message
- [ ] 6.6 Inject `ISkillRegistry` into `AetherSoul`; call `GetActiveSkills(prompt)` and append to system prompt
- [ ] 6.7 Pass session allowlist to `IToolExecutor.ExecuteAsync` on each tool call

## 7. Gateway

- [x] 7.1 `IChannel` and `InboundMessage` abstractions defined
- [x] 7.2 `MessageRouter` and `IMessageQueue` implemented
- [ ] 7.3 Implement `WebSocketChannel : IChannel` — ASP.NET Core WebSocket middleware, accept connections, normalize frames to `InboundMessage`, send `AgentResponse` back
- [ ] 7.4 Implement `TelegramChannel : IChannel` — long-polling loop, Telegram Bot API calls, normalize to `InboundMessage`, call `sendMessage` for responses
- [ ] 7.5 Register channels as `IHostedService` in `Program.cs`; skip Telegram if `telegram:bot_token` absent
- [ ] 7.6 Wire `MessageRouter` to dispatch from `IMessageQueue` → `AetherSoul.ProcessAsync` → response back to originating channel

## 8. Skill System

- [ ] 8.1 Create `Skills/` namespace with `ISkillRegistry`, `SkillDefinition`, `SkillContext`
- [ ] 8.2 Implement SKILL.md parser — YAML frontmatter (name, description, when_to_use, tools, auto_apply) + markdown body
- [ ] 8.3 Implement `SkillLoader` — scan configurable `skills/` directory at startup, register all valid `.md` files
- [ ] 8.4 Implement explicit trigger detection — `/<skill-name>` prefix lookup (O(1) dictionary)
- [ ] 8.5 Implement auto-trigger — precomputed TF-IDF vectors at load time; cosine similarity vs. user prompt; threshold 0.65
- [ ] 8.6 Implement `GetActiveSkills(prompt)` — returns list of matched skills (explicit first, then similarity)
- [ ] 8.7 Register `ISkillRegistry` in DI and inject into `AetherSoul`
- [ ] 8.8 Handle unknown slash command gracefully — respond with available skill names list

## 9. Multi-Agent Routing

- [ ] 9.1 Define `AgentRegistry` — keyed DI or dictionary of named `AetherSoul` instances
- [ ] 9.2 Add `gateway.agents` config section — maps channel source name → agent name
- [ ] 9.3 Implement `AgentRouter` — resolve agent by `InboundMessage.Source`, fall back to `"default"`
- [ ] 9.4 Prefix `group_folder` with agent name in `SessionManager` to isolate sessions (`<agent>/<group_folder>`)
- [ ] 9.5 Support per-agent config overrides (model, memory path, skills directory) via named options

## 10. Self-Improvement Workflow

- [ ] 10.1 Create `Improvement/` namespace with `IImprovementWorkflow`, `PromotionCandidate`, `RecidivismTracker`
- [ ] 10.2 Implement `DailyReviewCron : IHostedService` — runs at midnight UTC, inspects yesterday's sessions for corrections/failures
- [ ] 10.3 Implement `PromotionPipeline` — capture → classify → promote → apply → verify flow; writes to `patches/reflections-<date>.md`
- [ ] 10.4 Implement `RecidivismTracker` — fingerprint failures; after 3 occurrences, write `patches/skill-patch-<name>-<date>.md`
- [ ] 10.5 Implement benchmark gate — run `dotnet test` + `Aether.exe --smoke`; mark patch `VERIFIED` or `FAILED`
- [ ] 10.6 Implement visibility layer — log all candidate state transitions (PROPOSED → APPLIED → VERIFIED/FAILED) with timestamp
- [ ] 10.7 Implement file-based lock (`patches/.lock`) to prevent concurrent review runs
- [ ] 10.8 Register `DailyReviewCron` as `IHostedService` in `Program.cs` (guarded by `improvement:enabled` config flag)

## 11. Integration & Testing

- [ ] 11.1 End-to-end smoke test: start host with `--smoke` flag, verify clean exit
- [ ] 11.2 Integration test: send a WebSocket message, verify `AgentResponse` returned
- [ ] 11.3 Unit tests for `ProviderRouter` fallback logic and circuit breaker
- [ ] 11.4 Unit tests for `SkillRegistry` trigger detection (explicit + similarity)
- [ ] 11.5 Unit tests for memory promotion pipeline and MEMORY.md bounds enforcement
- [ ] 11.6 Integration test: tool schema validation rejects bad args
- [ ] 11.7 Verify `--prompt` harness still works end-to-end after all wiring changes
