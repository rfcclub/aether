## Architecture

Để hiện thực hóa trải nghiệm tối thượng đạt 80% tính năng của các CLI hàng đầu, kiến trúc của **Aether TUI 2.0** được chia làm hai phần cốt lõi tương tác thời gian thực qua WebSockets:

```
┌─────────────────────────────────┐               ┌─────────────────────────────────┐
│     Rust Client (aether-tui)    │               │       C# Backend (Aether)       │
├─────────────────────────────────┤               ├─────────────────────────────────┤
│ ┌───────────┐     ┌───────────┐ │               │ ┌─────────────┐ ┌─────────────┐ │
│ │  AppMode  │ ──> │ TUI Frame │ │  WebSockets   │ │  Gateway    │ │  AetherSoul │ │
│ │  Overlay  │     │ Rendering │ │ <───────────> │ │  Processor  │ │  LLM Loop   │ │
│ └───────────┘     └───────────┘ │  JSON Frames  │ └─────────────┘ └─────────────┘ │
│ ┌───────────┐     ┌───────────┐ │               │        │               │        │
│ │ Keyboard  │ ──> │ WebSocket │ │               │        ▼               ▼        │
│ │ Handler   │     │  Channel  │ │               │  Process Git   Cancel Token Source
│ └───────────┘     └───────────┘ │               │  & Filesystem  (Esc Key abort)  │
└─────────────────────────────────┘               └─────────────────────────────────┘
```

1. **Rust TUI Client (Ratatui + Crossterm):**
   - Sử dụng mô hình **State Machine** để quản lý trạng thái hiển thị (`AppMode`): `Normal` (chat), `ContextManager` (F4), `BrainstormWizard` (F5), và `GitDashboard` (F7).
   - Khi ở trạng thái overlay (`ContextManager` hoặc `GitDashboard`), bộ xử lý phím (Keyboard Handler) sẽ chặn và chuyển hướng các phím chức năng độc quyền (như `Space`, `a`, `d`, `c`, `Esc`) để phục vụ các hành động tương ứng mà không ảnh hưởng tới khung chat chính.
   - Giao thức truyền tin không đồng bộ thông qua `tokio::sync::mpsc` chuyển tiếp dữ liệu giữa luồng vẽ TUI và luồng WebSocket của `tokio-tungstenite`.

2. **C# Backend (Aether Server):**
   - Bổ sung các trình xử lý gói tin WebSocket (WebSocket Packet Processors) trong lớp `Gateway` để phân loại và điều phối các gói tin từ Rust client.
   - Quản lý vòng đời sinh của LLM trong `AetherSoul` thông qua một `CancellationTokenSource` được ánh xạ theo `SessionId`. Khi nhận được tín hiệu `cancel` từ client, máy chủ sẽ kích hoạt hủy bỏ token ngay lập tức để giải phóng tài nguyên.

## Components

### 1. Rust Client Components (New/Modified)
- `clients/aether-tui/src/app.rs` [MODIFY]: Mở rộng cấu trúc trạng thái ứng dụng (`App`, `AppMode`), thêm danh sách file context hiện thời và bộ nhớ tạm cho bảng điều khiển Git.
- `clients/aether-tui/src/ui/mod.rs` [MODIFY]: Đăng ký và phân phối vẽ khung hình dựa trên trạng thái `AppMode` hiện tại.
- `clients/aether-tui/src/ui/context_manager.rs` [NEW]: Vẽ giao diện overlay F4 hiển thị danh sách các file trong ngữ cảnh, có hộp thoại nhập đường dẫn khi nhấn `a`.
- `clients/aether-tui/src/ui/brainstorm_wizard.rs` [NEW]: Trình diễn bộ câu hỏi F5 từng bước và định dạng dữ liệu đầu ra thành markdown.
- `clients/aether-tui/src/ui/git_dashboard.rs` [NEW]: Bố cục chia đôi màn hình: bên trái vẽ danh sách file Git (nhóm Staged, Unstaged, Untracked), bên phải vẽ inline diff sử dụng màu sắc phân biệt (+ xanh lá, - đỏ).
- `clients/aether-tui/src/websocket.rs` [MODIFY]: Hỗ trợ gửi/nhận các khung JSON mở rộng (`context_update`, `cancel`, `git_status`, `stage_file`).

### 2. C# Backend Components (New/Modified)
- `src/Aether/Gateway/ChannelMessageProcessor.cs` [MODIFY]: Tiếp nhận các WebSocket frame loại `cancel`, `context_update`, `git_status`, `stage_file` và gọi hàm xử lý nghiệp vụ tương ứng.
- `src/Aether/Agent/AetherSoul.cs` [MODIFY]: Tích hợp `CancellationToken` vào quá trình sinh của mô hình LLM để dừng phát trực tiếp nội dung khi nhận yêu cầu hủy từ người dùng.

## Data Model

### Giao thức truyền tin WebSocket (JSON Frames)

1. **Context Update (`context_update`):**
```json
{
  "type": "context_update",
  "files": ["src/Aether/Program.cs", "tests/Aether.Tests/ProgrammingToolsTests.cs"]
}
```

2. **Cancel (`cancel`):**
```json
{
  "type": "cancel"
}
```

3. **Stage File (`stage_file`):**
```json
{
  "type": "stage_file",
  "file": "src/Aether/Program.cs",
  "stage": true
}
```

4. **Git Status Request (`git_status`):**
```json
{
  "type": "git_status"
}
```

5. **Git Status Response (`git_status_response`):**
```json
{
  "type": "git_status_response",
  "files": [
    { "path": "src/Aether/Program.cs", "status": "Staged" },
    { "path": "clients/aether-tui/src/main.rs", "status": "Modified" }
  ]
}
```

## Test Strategy

Để đảm bảo chất lượng tuyệt đối theo tinh thần TDD của LoomKit, các scenario được ánh xạ trực tiếp đến các bộ test sau:

| Scenario ID | Test File | Type | Description |
|-------------|-----------|------|-------------|
| `SC-CTX-01` | `clients/aether-tui/src/ui/context_manager_tests.rs` | Unit | Kiểm tra vẽ giao diện Context Manager overlay. |
| `SC-CTX-02` | `clients/aether-tui/src/ui/context_manager_tests.rs` | Unit | Test nhập đường dẫn và gửi gói tin `context_update`. |
| `SC-CTX-03` | `clients/aether-tui/src/ui/context_manager_tests.rs` | Unit | Test xoá file khỏi ngữ cảnh và gửi gói tin cập nhật. |
| `SC-CTX-04` | `clients/aether-tui/src/ui/context_manager_tests.rs` | Unit | Test dọn sạch toàn bộ danh sách file ngữ cảnh. |
| `SC-BST-01` | `clients/aether-tui/src/ui/brainstorm_tests.rs` | Unit | Kiểm tra vẽ bộ câu hỏi Socratic của F5 Wizard. |
| `SC-BST-02` | `clients/aether-tui/src/ui/brainstorm_tests.rs` | Unit | Kiểm tra xuất markdown proposal và tiêm vào chat buffer. |
| `SC-TDD-01` | `clients/aether-tui/src/ui/tdd_tests.rs` | Unit | Kiểm tra nhấn F6 chèn chính xác khuôn mẫu TDD mẫu. |
| `SC-GIT-01` | `clients/aether-tui/src/ui/git_tests.rs` | Unit | Kiểm tra phân bổ màn hình Git Dashboard và vẽ danh sách thay đổi. |
| `SC-GIT-02` | `clients/aether-tui/src/ui/git_tests.rs` | Unit | Test nhấn Space gửi gói tin `stage_file` và cập nhật giao diện. |
| `SC-CAN-01` | `tests/Aether.Tests/Gateway/CancellationTests.cs` | Integration | Test nhấn Esc gửi `cancel` và máy chủ dừng sinh LLM ngay lập tức. |

## Dependencies

- **Rust TUI Client:** Bổ sung thư viện `ratatui` (v0.29) và `crossterm` (v0.28) đã được khai báo, kết hợp `tokio-tungstenite` cho WebSocket.
- **C# Backend:** Không thêm thư viện ngoài nào khác, tận dụng các luồng xử lý không đồng bộ của .NET Core.

## Migration

Không có thay đổi phá vỡ cấu trúc cơ sở dữ liệu hiện thời (database schema). Việc mở rộng giao thức WebSocket hoàn toàn tương thích ngược với các phiên bản TUI cũ nhờ cơ chế bỏ qua các frame không nhận diện được (fallback ignore).
