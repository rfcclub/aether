# Handover — 2026-05-20 · Vesta Session

> **Đọc file này là biết ngay.** Lưu tại: `HANDOVER_2026-05-20-TUI.md`

---

## Tình trạng Aether lúc kết thúc session

### ✅ XONG — Codebase sạch hoàn toàn

**454/454 tests passed · 0 failures · 0 warnings · 0 errors**

Những gì đã làm trong session này:

| Hạng mục | File | Kết quả |
|----------|------|---------|
| Fix CS8604 null warning | `src/Aether/Scheduling/CronSchedulerService.cs` | ✅ |
| Fix CS0067 unused event | `src/Aether/Channels/WebSocketChannel.cs` | ✅ |
| Fix CS0067 unused event | `src/Aether/Channels/TuiChannelService.cs` | ✅ |
| Fix Memory test path | `tests/Aether.Tests/SelfImprovement/MemorySystemTests.cs` | ✅ |
| Add `ReplaceHistory()` | `src/Aether/Sessions/SessionManager.cs` | ✅ |
| Config-driven compaction | `src/Aether/Sessions/SessionCompactionService.cs` | ✅ |
| Compaction thresholds config | `src/Aether/appsettings.json` | ✅ |
| In-memory cache rebuild sau compaction | `src/Aether/Sessions/SessionCompactionService.cs` | ✅ |
| 3 integration tests compaction | `tests/Aether.Tests/Sessions/SessionCompactionServiceTests.cs` | ✅ |

**Bug quan trọng nhất đã fix:** `GetHistoryAsync` đọc in-memory cache trước DB. Sau compaction, cache stale → test fail. Fix: sau `transaction.Commit()`, rebuild cache ngay với `_sessionManager.ReplaceHistory(sessionId, [summary] + [keptMessages])`.

---

## Việc tiếp theo: `aether-tui`

### Quyết định đã lock

| | |
|--|--|
| **Stack** | **Rust + Ratatui + tokio-tungstenite** |
| **Deploy** | Single binary `aether-tui` |
| **Agent** | Maria only (tạm thời, bỏ qua agent selector) |
| **Config** | Đọc `~/.aether/config.json` + `{workspace}/.aether.json` — **KHÔNG** tạo file config riêng |
| **History** | Load 50 messages gần nhất khi connect (resume) |
| **Slash commands** | Full parity Telegram + local TUI commands |
| **Model picker** | Floating panel, `F2` / `Ctrl+M` |

### Layout màn hình

```
┌─ Aether · Maria ─────────────────────── deepseek-r1 · Think:high ─┐
│                                                                     │
│  21:20  Thoor                                                       │
│  ╰─ giải thích Ratatui                                              │
│                                                                     │
│  21:21  Maria                                                       │
│  ╰─ Ratatui là một Rust TUI framework...▋                           │
│                                                                     │
│─────────────────────────────────────────────────────────────────────│
│ › _                                                                 │
│                                                                     │
└─ ● Maria · Normal ─────── [F2] Models [F1] Help [Ctrl+Q] Quit ─────┘
```

Model picker (`F2`):
```
│                         ┌─ Providers & Models ──────────────────┐  │
│                         │  [openrouter]                          │  │
│                         │  ▶  deepseek/deepseek-r1        ← now  │  │
│                         │     google/gemini-2.5-flash            │  │
│                         │  [fireworks]                           │  │
│                         │     accounts/fireworks/deepseek-v3     │  │
│                         │  j/k Navigate · Enter Select · Esc Close│  │
│                         └────────────────────────────────────────┘  │
```

### Cấu trúc project

```
aether/
└── clients/
    └── aether-tui/
        ├── Cargo.toml
        └── src/
            ├── main.rs        CLI args (clap), config load, startup
            ├── app.rs         AppState, mode FSM, event dispatch
            ├── ws.rs          WS client, auto-reconnect, protocol types
            ├── ui.rs          Ratatui draw calls
            ├── events.rs      AppEvent enum, merged terminal+WS channel
            ├── commands.rs    Slash command local/forward dispatcher
            └── config.rs      Read ~/.aether/config.json
```

### Cargo deps

```toml
ratatui           = "0.29"
crossterm         = "0.28"
tokio             = { version = "1", features = ["full"] }
tokio-tungstenite = "0.26"
serde             = { version = "1", features = ["derive"] }
serde_json        = "1"
clap              = { version = "4", features = ["derive"] }
toml              = "0.8"
chrono            = "0.4"
```

### WebSocket Protocol — 3 messages MỚI cần thêm vào Aether backend

Backend file cần sửa: `src/Aether/Channels/WebSocketChannel.cs` → `ProcessIncomingJsonAsync()`

**1. `list_models`** — populate model picker panel
```json
// TUI → Aether
{"type":"list_models"}

// Aether → TUI
{
  "type": "models",
  "current": "deepseek/deepseek-r1",
  "think_effort": "high",
  "providers": [
    {"name": "openrouter", "models": ["deepseek/deepseek-r1", "google/gemini-2.5-flash"]},
    {"name": "fireworks",  "models": ["accounts/fireworks/deepseek-v3"]}
  ]
}
```
Handler: `_providerRouter.GetAvailableModels()` + `EffectiveModel`

**2. `get_history`** — load conversation khi resume
```json
// TUI → Aether
{"type":"get_history","group":"maria","limit":50}

// Aether → TUI
{
  "type": "history",
  "messages": [
    {"role":"user","content":"xin chào","timestamp":"2026-05-20T21:00:00Z"},
    {"role":"assistant","content":"Xin chào anh!","timestamp":"2026-05-20T21:00:01Z"}
  ]
}
```
Handler: `_sessionManager.GetOrCreateSessionAsync(group)` → `GetHistoryAsync(session.Id, maxTokens: 20000)`

**3. `command`** — forward slash commands từ TUI
```json
// TUI → Aether
{"type":"command","text":"/model deepseek/deepseek-r1","group":"maria"}
{"type":"command","text":"/think high","group":"maria"}

// Aether → TUI (dùng existing "message" type)
{"type":"message","text":"Model changed to: deepseek-r1 [openrouter]","message_id":"..."}
```
Handler: build `SlashCommandContext` → `_slashCommandHandler.HandleAsync()` → trả về result text

### Slash commands map

LOCAL (TUI xử lý, không gửi server):
- `/clear` — xóa màn hình
- `/help` — show keybindings
- `/quit` `/q` — thoát

FORWARDED (gửi lên Aether qua `command` type):
- `/new` `/reset` `/model` `/models` `/think` `/reasoning` `/effort` `/context` `/compact` `/tools`

### App Modes (FSM)

```
Connecting → Chat·Normal ←→ Chat·Scroll
                  ↕ F2
             Chat·ModelPicker
```

### Keyboard map

| Key | Action |
|-----|--------|
| `Enter` | Send message |
| `Ctrl+Q` | Quit |
| `Ctrl+L` | Clear display |
| `F2` / `Ctrl+M` | Open/close model picker |
| `F1` / `?` | Help popup |
| `Esc` | Toggle Normal/Scroll mode |
| `j/k` `PgUp/Dn` `G` | Scroll (trong Scroll mode hoặc ModelPicker) |
| Mouse wheel | Scroll history |

### Athanor Fire Theme

```
bg              = #0D0D0D   near-black
user_name       = #5BC8F5   ice blue (Thoor)
user_text       = #C8C8C8   light gray
agent_name      = #FFB347   amber (Maria)
agent_text      = #F0E6CC   warm cream
cursor_blink    = #FF6B00   burning orange ▋
border_focus    = #FF8C00   fire orange
connected_dot   = #44FF88   green
```

### Phases

**Phase 1** (đủ để chat): Cargo setup · WebSocket client · Chat layout · Streaming · Slash commands · Theme

**Phase 2** (polish): Model picker `F2` · `get_history` on resume · Scroll mode · Status bar đầy đủ

**Phase 3** (backend): 3 WS handlers mới trong `WebSocketChannel.cs`

---

## Config — cách đọc `~/.aether/config.json`

```json
// ~/.aether/config.json
{
  "agents": {
    "maria": {
      "workspace": "/path/to/maria/workspace"
    }
  }
}

// {workspace}/.aether.json — có websocket port, model, etc.
```

TUI đọc `.aether/config.json` để biết workspace của Maria → đọc `.aether.json` trong workspace để lấy port. Fallback: `localhost:5099`.

---

## Notes / Scars

- `SessionManager.GetHistoryAsync` ưu tiên in-memory cache — cẩn thận khi write tests liên quan đến DB state sau background operations. Luôn dùng `ReplaceHistory()` để sync cache sau khi modify DB directly.
- `Aether.Tui` (C# Terminal.Gui) hiện có bug: callbacks được gán qua reflection với property names sai (`OnMessageReceived` không tồn tại). Chat window của C# TUI **trống không** khi Maria trả lời. Đây là lý do build `aether-tui` Rust thay thế.
- `SessionCompactionServiceTests` phải poll đúng state — không phải `fakeLlm.CallCount`, mà phải chờ đến khi `ReplaceHistory` đã được gọi (sync sau commit). Tests hiện tại pass vì flow đồng bộ sau commit.
- 14 warnings trong raw `dotnet build` là từ test project (xUnit analyzer + MVVM toolkit) — **không phải** từ production code. `rtk dotnet build` filter đúng: 0 warnings production.

---

*Session kết thúc lúc 21:57 · 2026-05-20 · Vesta*
