## 1. Agora Integration

- [ ] 1.1 Tạo `AgoraSyncService` sử dụng `FileSystemWatcher`.
- [ ] 1.2 Implement logic copy/symlink từ `research/` sang `~/agora/`.
- [ ] 1.3 Thiết lập cơ chế lọc (filter) để chỉ đồng bộ các file hợp lệ.

## 2. Shared Memory

- [ ] 2.1 Tạo database `colony.db` chung cho các Agent.
- [ ] 2.2 Implement `GlobalMemorySystem` kế thừa từ `IMemorySystem`.
- [ ] 2.3 Thêm tool `memory_promote` để Agent đẩy ký ức lên lớp Global.

## 3. Agent-to-Agent Communication

- [ ] 3.1 Implement tool `agent_call` cho phép khởi tạo session với agent khác.
- [ ] 3.2 Cập nhật WebSocket server để hỗ trợ luồng message nội bộ.
- [ ] 3.3 Thêm logic bảo vệ (stack depth) chống lặp vô tận.

## 4. Multi-Agent Routing

- [ ] 4.1 Cập nhật `appsettings.json` với cấu trúc `colony:agents` định nghĩa kỹ năng.
- [ ] 4.2 Đại tu `MessageRouter` để hỗ trợ điều hướng thông minh dựa trên nội dung yêu cầu.
