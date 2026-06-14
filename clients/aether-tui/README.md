# aether-tui — User Guide

> Terminal UI client cho Aether AI · Athanor Fire theme 🔥

---

## Yêu cầu

- Aether backend đang chạy (port 5099 mặc định)
- Linux terminal với `TERM=xterm-256color` hoặc kitty/alacritty
- `~/.aether/config.json` (tùy chọn — xem phần Config)

---

## Khởi động nhanh

### Cách 1 — Script tự động (khuyến nghị)

```bash
# Tự động kill server cũ → start server mới → launch TUI
cd /home/thoor/repo/aether
./clients/aether-tui/tui.sh
```

Script sẽ:
1. Kill bất kỳ process nào đang chiếm port 5099
2. Start Aether backend ở background
3. Đợi backend sẵn sàng (tối đa 10s)
4. Launch `aether-tui`
5. Tự động kill backend khi TUI thoát

```bash
# Build binary trước khi chạy (lần đầu hoặc sau khi sửa code)
./clients/aether-tui/tui.sh --build

# Kết nối đến một group khác
./clients/aether-tui/tui.sh --group general

# Kết nối đến backend remote
./clients/aether-tui/tui.sh --url ws://192.168.1.10:5099/ws
```

### Cách 2 — Thủ công

```bash
# Terminal 1: Start backend
cd /home/thoor/repo/aether
./run.sh

# Terminal 2: Launch TUI
./clients/aether-tui/target/release/aether-tui
```

---

## CLI Arguments

```
aether-tui [OPTIONS]

Options:
  --url <URL>      WebSocket URL override
                   Default: ws://localhost:5099/ws
  --group <NAME>   Agent group to connect to
                   Default: maria
  --help           Show help
```

---

## Giao diện

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
└─ ● Maria · Normal ──────── [F2] Models [F1] Help [Ctrl+Q] Quit ────┘
```

**Màu sắc (Athanor Fire theme):**
- 🟦 Tên Thoor — `#5BC8F5` ice blue
- 🟠 Tên Maria — `#FFB347` amber
- 🔶 Cursor streaming `▋` — `#FF6B00` fire orange
- 🟢 Connected dot `●` — `#44FF88` green
- ⚫ Background — `#0D0D0D` near-black

---

## Keyboard Shortcuts

### Normal Mode

| Phím | Hành động |
|------|-----------|
| `Enter` | Gửi message |
| `Ctrl+Q` | Thoát (terminal được restore) |
| `Ctrl+L` | Clear màn hình (không xóa history server) |
| `Esc` | Vào Scroll mode |
| `F1` hoặc `?` | Hiện help popup |
| `F2` hoặc `Ctrl+M` | Mở model picker |
| `Backspace` | Xóa ký tự |

### Scroll Mode (nhấn `Esc` để vào)

| Phím | Hành động |
|------|-----------|
| `k` / `↑` | Cuộn lên (messages cũ hơn) |
| `j` / `↓` | Cuộn xuống (messages mới hơn) |
| `PgUp` | Cuộn lên 10 dòng |
| `PgDn` | Cuộn xuống 10 dòng |
| `G` | Nhảy xuống cuối (latest) |
| `gg` | Nhảy lên đầu (oldest) |
| Mouse wheel | Cuộn lên/xuống 3 dòng |
| `Esc` | Thoát Scroll mode → Normal |
| Ký tự bất kỳ | Thoát Scroll + nhập ký tự đó |

### Model Picker (`F2`)

| Phím | Hành động |
|------|-----------|
| `j` / `↓` | Model tiếp theo |
| `k` / `↑` | Model trước |
| `Enter` | Chọn model |
| `Esc` | Đóng picker |

---

## Slash Commands

### LOCAL (xử lý tại TUI, không gửi server)

| Command | Hành động |
|---------|-----------|
| `/clear` | Clear màn hình |
| `/help` | Hiện help popup |
| `/quit` hoặc `/q` | Thoát |

### FORWARDED (gửi lên Aether backend)

| Command | Hành động |
|---------|-----------|
| `/model <name>` | Đổi model (e.g. `/model google/gemini-2.5-flash`) |
| `/models` | Liệt kê models available |
| `/think <low\|medium\|high>` | Đặt thinking effort |
| `/new` | Tạo session mới |
| `/reset` | Reset session |
| `/context` | Xem context hiện tại |
| `/compact` | Compact conversation history |
| `/tools` | Liệt kê tools available |

---

## Config

TUI tự động resolve WebSocket URL theo thứ tự:

1. **CLI flag** `--url ws://...` — override tất cả
2. **Env var** `AETHER_WS_URL=ws://host:port/ws`
3. **Config file** `~/.aether/config.json`:
   ```json
   {
     "agents": {
       "maria": {
         "workspace": "/path/to/maria/workspace"
       }
     }
   }
   ```
   → đọc `{workspace}/.aether.json` → lấy `websocket.port`
4. **Fallback** `ws://localhost:5099/ws`

---

## Build từ source

```bash
cd /home/thoor/repo/aether/clients/aether-tui

# Debug build (nhanh hơn, để develop)
~/.cargo/bin/cargo build

# Release build (optimized, để dùng thực tế)
~/.cargo/bin/cargo build --release

# Binary output:
# clients/aether-tui/target/release/aether-tui  (~2.9MB)
```

---

## Troubleshooting

**TUI không kết nối được:**
```bash
# Kiểm tra backend có chạy không
fuser 5099/tcp

# Xem log backend
tail -f /tmp/aether-server.log

# Kill backend cũ và restart
fuser -k 5099/tcp && ./run.sh
```

**Màn hình bị vỡ sau khi Ctrl+C đột ngột:**
```bash
reset
```

**Streaming không hiện — chỉ thấy message complete:**
Aether backend có thể đang chạy ở non-streaming mode. Kiểm tra `appsettings.json` xem streaming được enable không.

**Model picker hiện "Loading models…":**
Backend cần Phase 3 WS handlers (đã implement). Restart backend để load code mới.
