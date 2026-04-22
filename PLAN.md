# Aether — Project Plan
> **Author:** Miriam (Project Lead)  
> **Date:** 2026-04-19  
> **Based on:** ARCHITECTURE.md by Maria  
> **Status:** Active

---

## Overview

Aether là rewrite của NanoClaw sang .NET 9. Phase 1 là Core Rewrite — mục tiêu: một agent loop hoạt động được, kết nối Telegram, sandbox bash, và session management cơ bản.

Miriam điều phối. Erza và 2B implement song song theo hai track độc lập.

---

## Parallel Tracks

### Track A — Erza: Infrastructure Layer

**Scope:** Channel Manager + Message Router + SQLite + File Structure

**Files:** `src/Aether/Channels/`, `src/Aether/Routing/`, `src/Aether/Data/`

**Deliverables:**

1. **Project scaffold**
   - `Aether.sln` + `src/Aether/Aether.csproj`
   - Folder structure theo ARCHITECTURE.md § 4.2
   - `Program.cs` với DI container + host setup

2. **SQLite schema**
   - `Data/Schema.sql` theo ARCHITECTURE.md § 4.1
   - `Data/AetherDb.cs` — connection, migration, basic repo pattern

3. **IChannel interface + Telegram channel**
   - `Channels/IChannel.cs` theo spec § 3.1
   - `Channels/Telegram/TelegramChannel.cs` dùng Telegram.Bot
   - Kết nối, nhận message, gửi message, typing indicator

4. **Message Router**
   - `Routing/MessageRouter.cs` theo spec § 3.2
   - Normalize inbound message
   - Match group → enqueue vào `System.Threading.Channels`

**Constraint:** Không đụng `Agent/`, `Memory/`, `Sessions/`, `Providers/`

**Output:** Erza `SendMessage` Miriam khi xong từng deliverable, kèm summary những gì đã làm.

---

### Track B — 2B: Agent Layer

**Scope:** AetherSoul (core loop) + LLM Provider + Tool Executor + Memory + Session Manager

**Files:** `src/Aether/Agent/`, `src/Aether/Providers/`, `src/Aether/Memory/`, `src/Aether/Sessions/`

**Deliverables:**

1. **LLM Provider (Direct API)**
   - `Providers/ILLMProvider.cs` — abstraction
   - `Providers/OpenRouterProvider.cs` — `HttpClient` + streaming
   - Tool calling support (Anthropic tool use format)

2. **Tool Executor (sandbox)**
   - `Agent/ToolExecutor.cs` theo spec § 3.4
   - `bwrap` wrapper trên Linux, fallback Process trên Windows
   - Built-in tools: `bash`, `read`, `write`, `edit`, `glob`, `grep`
   - Timeout + resource limits

3. **Session Manager**
   - `Sessions/Session.cs` + `Sessions/SessionManager.cs` theo spec § 3.6
   - Load/save history từ SQLite
   - Compaction khi > 150K tokens

4. **Memory System (Layer 1)**
   - `Memory/IMemorySystem.cs` + `Memory/FileMemory.cs`
   - Load CLAUDE.md hierarchy (global + group)
   - Chưa cần Vector DB

5. **AetherSoul (core loop)**
   - `Agent/AetherSoul.cs` theo spec § 3.3
   - Nhận session + prompt → gọi LLM → execute tools → trả response
   - Tích hợp SessionManager + MemorySystem + ToolExecutor

**Constraint:** Không đụng `Channels/`, `Routing/`, `Data/` (dùng interface, không tự implement)

**Output:** 2B `SendMessage` Miriam khi xong từng deliverable, kèm summary.

---

## Integration Points

Erza và 2B dùng interface để decouple:

| Interface | Owner | Consumer |
|-----------|-------|----------|
| `IChannel` | Erza | Miriam (wiring) |
| `IMessageQueue` | Erza | 2B (đọc message) |
| `ILLMProvider` | 2B | AetherSoul |
| `IToolExecutor` | 2B | AetherSoul |
| `ISessionManager` | 2B | AetherSoul |
| `IMemorySystem` | 2B | AetherSoul |
| `AetherDb` | Erza | 2B (inject via DI) |

Miriam wire tất cả trong `Program.cs` sau khi cả hai track xong.

---

## Sequence

```
Day 1
├── Erza: scaffold + SQLite schema
├── 2B: ILLMProvider + OpenRouterProvider
│
Day 2
├── Erza: IChannel + TelegramChannel
├── 2B: ToolExecutor (bwrap sandbox)
│
Day 3
├── Erza: MessageRouter + queue
├── 2B: SessionManager + MemorySystem
│
Day 4
├── 2B: AetherSoul core loop
├── Miriam: integration + wiring
│
Day 5
├── End-to-end test: Telegram → AetherSoul → response
└── Handoff to Maria for test suite
```

---

## Coordination Rules

- **Không edit cùng file** — mỗi track có folder riêng
- **Interface first** — nếu cần dùng component của track kia, define interface trước, mock sau
- **Báo Miriam** khi xong deliverable hoặc bị blocked
- **Miriam không code** — chỉ coordinate, review, unblock, wire final

---

## Open Questions (cần quyết định trước khi bắt đầu)

| # | Câu hỏi | Quyết định |
|---|---------|-----------|
| 1 | Dùng Telegram.Bot hay TDLib? | Telegram.Bot (đơn giản hơn) |
| 2 | `IMessageQueue` dùng gì? | `System.Threading.Channels` |
| 3 | bwrap unavailable → fallback? | Restricted `Process` (no network, temp dir) |
| 4 | Config load từ đâu? | `appsettings.json` + env vars qua `IConfiguration` |

---

*Document Version: 1.0*  
*Last Updated: 2026-04-19*  
*Author: Miriam*
