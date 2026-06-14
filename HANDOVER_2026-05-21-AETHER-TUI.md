# Handover — 2026-05-21 · Vesta Session

> **Đọc file này là biết ngay.** Lưu tại: `HANDOVER_2026-05-21-AETHER-TUI.md`

---

## Tình trạng Aether lúc kết thúc session

### ✅ XONG — aether-tui Rust client + Backend WS handlers

| Hạng mục | Kết quả |
|----------|---------|
| **Phase 1**: Rust TUI cơ bản | ✅ Build OK, binary 2.9MB |
| **Phase 2**: Model picker + scroll + history resume | ✅ Build OK, 0 errors 0 warnings |
| **Phase 3**: Backend WS handlers (`list_models`, `get_history`, `command`) | ✅ Merged vào main |
| **Tests**: 454 tests C# | ✅ 454/454 (1 flaky race condition — pre-existing) |
| **Docs**: README + tui.sh script | ✅ |
| **OpenSpec**: 3 changes archived | ✅ `openspec/changes/archive/2026-05-21-*` |

---

## Files mới trong session này

### Rust client (mới hoàn toàn)
```
clients/aether-tui/
├── Cargo.toml              — deps: ratatui 0.29, crossterm 0.28, tokio 1, tokio-tungstenite 0.26
├── tui.sh                  — launch script (kill old server → start fresh → TUI)
├── README.md               — user guide đầy đủ
└── src/
    ├── main.rs             — CLI (clap), event loop, keyboard handler
    ├── app.rs              — AppState, AppMode FSM, event handler
    ├── ws.rs               — WS client, auto-reconnect, streaming parser
    ├── ui.rs               — Ratatui draw, Athanor Fire theme, model picker overlay
    ├── events.rs           — AppEvent enum, Message, ModelsPayload
    ├── commands.rs         — LOCAL vs FORWARDED slash command dispatch
    └── config.rs           — config chain resolver
```

### C# backend (modified)
```
src/Aether/Channels/WebSocketChannel.cs   — +3 new WS message handlers
src/Aether/Program.cs                     — updated WebSocketChannel DI registration
```

---

## Cách chạy

```bash
# Nhanh nhất — script tự động
cd /home/thoor/repo/aether
./clients/aether-tui/tui.sh

# Lần đầu hoặc sau khi update code
./clients/aether-tui/tui.sh --build

# Manual (2 terminals)
# T1: ./run.sh
# T2: ./clients/aether-tui/target/release/aether-tui
```

---

## Architecture tóm tắt

### FSM AppMode
```
Connecting
    ↓ AppEvent::Connected
Normal ←→ Esc ←→ Scroll
  ↕ F2/Ctrl+M
ModelPicker
  ↕ F1/?
ShowHelp
```

### Event loop
```
tokio::select!
├── app_rx (mpsc) ← ws_task gửi AppEvent
└── crossterm::EventStream ← keyboard/mouse
```

### WS Protocol (inbound từ Aether)
| type | Xử lý |
|------|-------|
| `chunk` | append → `streaming_buf` → render với `▋` |
| `message` | flush `streaming_buf` → push to `messages` |
| `typing` | status bar indicator |
| `models` | → `AppState.models` → model picker data |
| `history` | prepend historical messages (is_historical=true) |
| `error` | render với màu đỏ |

### WS Protocol (outbound từ TUI)
| type | Khi nào |
|------|---------|
| `{"type":"message","text":"...","group":"maria"}` | User nhấn Enter |
| `{"type":"command","text":"/model ...","group":"maria"}` | Slash command forwarded |
| `{"type":"list_models"}` | Ngay sau connect |
| `{"type":"get_history","group":"maria","limit":50}` | Ngay sau connect |

---

## Keyboard map (tóm tắt)

| Key | Normal | Scroll | ModelPicker |
|-----|--------|--------|-------------|
| `Enter` | Send msg | — | Select model |
| `Esc` | → Scroll | → Normal (reset offset) | Close picker |
| `F2`/`Ctrl+M` | Open picker | — | — |
| `F1`/`?` | Help popup | — | — |
| `Ctrl+Q` | Quit | Quit | Quit |
| `Ctrl+L` | Clear display | — | — |
| `j`/`k` | — | Scroll ↓/↑ | Navigate ↓/↑ |
| `G`/`gg` | — | Bottom/Top | — |
| `PgUp/Dn` | — | ±10 lines | — |
| Mouse wheel | Scroll ±3 | Scroll ±3 | — |

---

## Scars / Gotchas

### 1. `ProviderRouter` và `SlashCommandHandler` là concrete classes
Không có interface. Inject trực tiếp vào `WebSocketChannel` constructor. Các handler dùng `null`-guard: nếu dep null (test constructors không truyền đủ) thì trả về error message thay vì crash.

### 2. Flaky test `CompactSession_ConcurrentEnqueueing_IsSafe`
Test này có race condition inherent — fail khoảng 1/5 lần khi chạy toàn suite. Chạy đơn lẻ thì luôn pass. **Không phải regression.** Handover cũ đã note điều này.

### 3. WebSocket chunk type là `"chunk"` (không phải `"streaming_chunk"`)
ws.rs handle cả hai để an toàn, nhưng backend thực tế gửi `"chunk"`.

### 4. `history_loaded` flow
Phase 2: `history_loaded = false` khi init. `send_message()` trả về `None` nếu chưa loaded (input locked). Sau 2s timeout hoặc khi nhận `history` response → `history_loaded = true` → input unlock. **HistoryLoaded event là idempotent** (call thứ 2 với vec rỗng là no-op).

### 5. `ws_task` signature Phase 2
Signature: `ws_task(url: String, group: String, tx: mpsc::Sender<AppEvent>, rx: mpsc::Receiver<String>)` — `group` là param thứ 2 (thêm vào trong Phase 2). Call site trong `main.rs` đã update.

### 6. Smoke test chưa chạy
Tasks 7.2/7.3 (Phase 2) và Task 6.2 (Phase 3) là live smoke tests cần terminal thật. Code đã wire đúng nhưng chưa verify end-to-end với real backend. **Cần verify khi có terminal.**

---

## Việc tiếp theo (nếu có)

| Priority | Task |
|----------|------|
| 🔴 | **Live smoke test** — chạy `./clients/aether-tui/tui.sh` và verify end-to-end |
| 🟡 | Fix flaky `CompactSession_ConcurrentEnqueueing_IsSafe` (race condition với Task.Delay) |
| 🟡 | Test file cho `WebSocketChannel` handlers (deferred — cần full DI integration test setup) |
| 🟢 | Agent selector — hiện tại hardcode "maria", cân nhắc thêm `--agent` flag |
| 🟢 | Install script — copy binary → `~/.local/bin/aether-tui` |

---

## OpenSpec archived

```
openspec/changes/archive/
├── 2026-05-21-aether-tui-phase1-rust-client/
├── 2026-05-21-aether-tui-phase2-polish/
└── 2026-05-21-aether-tui-phase3-backend-ws/
```

Specs synced to `openspec/specs/`:
`tui-rust-client`, `tui-config-loader`, `tui-websocket-client`,
`tui-model-picker`, `tui-history-resume`, `tui-scroll-mode`,
`ws-list-models`, `ws-get-history`, `ws-command-forward`

---

*Session kết thúc lúc 17:44 · 2026-05-21 · Vesta*
