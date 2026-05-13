# Forge Tasks — Aether Hardening

## 1. Build & Test Baseline

- [ ] `dotnet build` — verify clean compilation (all configurations: Debug + Release)
- [ ] `dotnet test` — run full test suite, record baseline pass/fail counts
- [ ] Check test coverage: `dotnet test --collect:"XPlat Code Coverage"`
- [ ] Fix any failing/flaky tests before proceeding

## 2. AetherSoul LLM Loop — Edge Case Stress

- [ ] Test with each provider type (Anthropic, OpenRouter, Fireworks, Generic HTTP)
- [ ] Empty system prompt → should not crash
- [ ] Max tool iterations (8) — does it bail out cleanly or infinite-loop?
- [ ] Tool call returns error → does error propagate to context or crash?
- [ ] Hook blocks (PreLlmCall, PostLlmCall, PreToolUse deny) → verify blocked cleanly
- [ ] Concurrent prompt calls on same session → race condition audit
- [ ] CancellationToken mid-LLM-call → graceful abort or orphaned state?
- [ ] Model returns malformed JSON tool calls → handled or crash?

## 3. Memory — Dual-Write Integrity

- [ ] Write to FileMemory, verify markdown file exists with correct content
- [ ] Write to SqliteMemorySystem, verify neuron + synapse rows
- [ ] Concurrent writes to both systems → consistency check
- [ ] FileMemory path traversal — verify sandbox containment
- [ ] Memory search with special characters in query → SQL injection audit
- [ ] Empty write, very long content, binary content → graceful handling

## 4. Plugin System — Isolation & Security

- [ ] Load plugin with valid manifest → all assets registered (hooks, tools, skills, cron)
- [ ] Load plugin with missing manifest → clean error, not crash
- [ ] Load plugin with invalid JSON manifest → clean error
- [ ] Malicious plugin: tries to read files outside workspace → verify sandbox
- [ ] Plugin with circular dependencies → handled?
- [ ] Unload/reload plugin cycle → memory leak check
- [ ] Permission gate: plugin requests denied permission → verify blocked

## 5. Channels — Multi-Channel Concurrency

- [ ] Telegram channel receives message → routes to MessageRouter → AetherSoul responds
- [ ] WebSocket channel connects → receives messages → responds
- [ ] Both channels active simultaneously → no cross-talk, no dropped messages
- [ ] Channel disconnects mid-response → cleanup, no orphaned state
- [ ] ChannelAccess: verify allowlist/blocklist enforcement
- [ ] Slash command dispatch: `/help`, `/model`, `/agent` → correct handler

## 6. Cron + Kairos — Scheduling Correctness

- [ ] Cron task fires at scheduled time → executes → logs result
- [ ] Cron task with invalid frontmatter → parsed correctly or skipped with error
- [ ] Overlapping cron tasks → scheduler prevents concurrent execution?
- [ ] Kairos watch: file created/modified → notification fires within cooldown window
- [ ] Kairos watch: rapid file changes → cooldown respected, no spam
- [ ] Cron directory empty → no crash, no spurious runs

## 7. Session Continuity

- [ ] Start session, write messages, stop → restart → messages restored from DB
- [ ] Session across provider switches → state preserved
- [ ] SessionManager.ResumeAsync with nonexistent session → handled?
- [ ] Concurrent sessions (same agent, different groups) → isolated state

## 8. Self-Improvement Pipeline

- [ ] BenchmarkGate runs tests → returns pass/fail counts correctly
- [ ] SelfImprovementService generates patch candidate → writes to patches/
- [ ] PipelineTracker records attempt → state file correct
- [ ] Patch application failure → rolls back cleanly?
- [ ] DailyReview fires → consolidates, doesn't crash overnight

## 9. Tool Execution — Safety Boundaries

- [ ] Bash tool: `rm -rf /` → blocked by sandbox
- [ ] Bash tool: path traversal (`../../etc/passwd`) → blocked
- [ ] File tools: read/write outside workspace → blocked
- [ ] WebFetch: internal/localhost URLs → blocked?
- [ ] Tool execution timeout → killed, not hung
- [ ] Parameter validation: missing required params → error, not crash

## 10. Agent Identity — Integrity Verification

- [ ] First boot: IntegritySigner generates Ed25519 keypair
- [ ] Boot files signed → VerifyAllAsync passes
- [ ] Boot file modified externally → VerifyAllAsync reports failure
- [ ] Tampered file re-signed automatically → chain preserved
- [ ] AETHER_SKIP_INTEGRITY=1 → bypass works

---

## Forge Principle

> Athanor does not accumulate. It refines. Put each subsystem in fire. What survives unchanged is ready. What breaks reveals where the forge must work harder. What crumbles was never solid to begin with.

Aria built the architecture. Vesta proves it.
