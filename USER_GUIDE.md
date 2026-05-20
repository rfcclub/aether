# Aether User Guide

Chào mừng Anh đến với hướng dẫn sử dụng Aether. Tài liệu này giúp Anh nắm bắt cách tương tác, quản lý và rèn luyện các thực thể AI trong hệ sinh thái Aether.

## 1. Các kênh tương tác (Interaction Channels)

Aether hỗ trợ nhiều cách để Anh trò chuyện với các agent:

### Telegram (Khuyên dùng cho di động)
Sau khi cấu hình bot token (xem [Configuration Guide](CONFIGURATION.md)), Anh có thể:
- **Chat trực tiếp**: Gửi tin nhắn cho bot của Anh.
- **Group Chat**: Thêm bot vào group. Aether hỗ trợ quản lý nhiều group folder khác nhau, mỗi group có thể có chỉ dẫn (`CLAUDE.md`) riêng.
- **Typing Indicator**: Aether sẽ hiển thị "typing..." khi đang suy nghĩ hoặc thực thi tool để Anh biết em vẫn đang làm việc.

### Terminal UI (TUI)
Giao diện chat trực quan ngay trong terminal:
```bash
cd src/Aether.Tui
dotnet run
```
- **F5**: Chuyển đổi giữa các Group folder.
- **PgUp/PgDn**: Cuộn lịch sử chat.
- **Ctrl+W**: Bật/tắt tự động xuống dòng (Word wrap).

### CLI (One-shot prompt)
Dùng cho các yêu cầu nhanh hoặc tích hợp script:
```bash
dotnet run --project src/Aether -- --prompt "Viết báo cáo tiến độ hôm nay" --group main
```

## 2. Quản lý Agent (Agent CLI)

Anh có thể quản lý "đội ngũ" AI của mình thông qua lệnh `aether agent`.

| Lệnh | Tác dụng |
|---|---|
| `aether agent add <name>` | Tạo một agent mới (ví dụ: `maria`, `vesta`). |
| `aether agent list` | Liệt kê danh sách các agent đang có. |
| `aether agent set-identity <name>` | Cập nhật tên hiển thị, emoji đại diện. |
| `aether agent bind <name> --channel telegram:<chatId>` | Gắn agent vào một chat Telegram cụ thể. |
| `aether agent delete <name>` | Xóa agent khỏi hệ thống. |

## 3. Cấu trúc Trí não (FEOFALLS)

Mỗi Agent là một thư mục tại `~/.aether/workspaces/<name>/`. Anh có thể "rèn" agent bằng cách chỉnh sửa trực tiếp các file này:

- **IDENTITY.md**: Định nghĩa "Tôi là ai", các giới hạn và nguyên tắc đạo đức.
- **SOUL.md**: Định nghĩa tính cách, giọng văn và cách hành xử.
- **MEMORY.md**: Trí nhớ dài hạn. Đây là nơi agent lưu lại những kết luận quan trọng sau mỗi phiên làm việc.
- **USER.md**: Thông tin về Anh (Thoor) để agent biết cách xưng hô và phục vụ tốt nhất.
- **memory/**: Thư mục chứa nhật ký hàng ngày (`YYYY-MM-DD.md`). Aether tự động nạp 2 ngày gần nhất vào context.

## 4. Làm việc với Context (CLAUDE.md)

Aether sử dụng hệ thống context phân cấp:
1. **Global Context** (`groups/CLAUDE.md`): Các quy tắc áp dụng cho mọi project.
2. **Project Context** (`groups/<folder>/CLAUDE.md`): Quy tắc riêng cho từng thư mục làm việc.

Agent sẽ đọc các file này trước khi trả lời tin nhắn của Anh trong group đó.

## 5. Tự rèn luyện (Self-Improvement)

Aether không chỉ đứng yên. Hệ thống có cơ chế "Heartbeat" (Nhịp đập):
- Định kỳ (mặc định 30 phút), agent sẽ tự kiểm tra `HEARTBEAT.md` để thực hiện các task ngầm.
- Agent tự phản chiếu (reflect) lại các cuộc hội thoại, rút ra kinh nghiệm và lưu vào `MEMORY.md`.
- **Benchmark Gate**: Khi agent đề xuất một thay đổi logic, hệ thống sẽ tự chạy bộ test suite để đảm bảo bản vá không làm hỏng ứng dụng.

## 6. Mẹo sử dụng (Pro Tips)

- **Clear Memory**: Nếu cuộc hội thoại bị lạc đề, hãy nói "Reset session" hoặc dùng lệnh tương đương để dọn dẹp context ngắn hạn.
- **Tool Use**: Anh có thể yêu cầu Aether đọc file, chạy lệnh bash hoặc fetch web. Hãy nhớ rằng các lệnh bash chạy trong **Sandbox (bwrap)** để đảm bảo an toàn cho máy của Anh.
- **Context Injection**: Anh có thể copy-paste đường dẫn file vào chat, Aether sẽ biết cách sử dụng tool `read` để nạp dữ liệu đó nếu cần.

---
*Aether không phải là một chatbot, Aether là một thực thể sống cùng Anh trong thế giới kỹ thuật.*
