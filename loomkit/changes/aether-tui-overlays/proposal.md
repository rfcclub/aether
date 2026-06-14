## Why

Hiện tại, Aether Client mới chỉ hỗ trợ một TUI (Terminal User Interface) rất cơ bản viết bằng Rust (`clients/aether-tui`). Nó thiếu các công cụ tương tác trực quan để quản lý ngữ cảnh tệp tin, soạn thảo tài liệu, quản lý mã nguồn Git, và huỷ lệnh LLM đang chạy như Antigravity CLI hay Gemini CLI. Việc nâng cấp TUI lên phiên bản 2.0 sẽ giúp các "chiến sĩ" của anh yêu thao tác lập trình cực kỳ mượt mà, đạt 80% tính năng của các CLI cao cấp nhất.

## What Changes

Chúng ta sẽ nâng cấp toàn diện ứng dụng TUI Rust (`clients/aether-tui`) với các tính năng:
- **F4 Context Files Manager Overlay**: Giao diện trực quan để xem, thêm (`a`), xoá (`d`), và dọn sạch (`c`) các tệp tin trong ngữ cảnh làm việc hiện tại, đồng bộ hóa qua WebSocket.
- **F5 Socratic Brainstorming Wizard**: Giao diện từng bước hướng dẫn người dùng phác thảo giải pháp và so sánh các hướng kiến trúc khác nhau một cách logic nhất.
- **F6 TDD Template Injector**: Phím tắt giúp chèn khuôn mẫu RED-GREEN-REFACTOR của LoomKit vào khung chat để đảm bảo kỷ luật TDD tuyệt đối.
- **F7 Git Dashboard**: Bảng điều khiển Git toàn màn hình hiển thị danh sách Staged/Unstaged ở cột trái và Inline Diff ở cột phải, hỗ trợ Stage/Unstage bằng phím `Space` và commit bằng phím `c`.
- **LLM Interruption (Esc)**: Cho phép người dùng nhấn phím `Esc` khi LLM đang stream để huỷ ngay lập tức quá trình sinh của mô hình ở cả client và server thông qua tín hiệu hủy trên WebSocket.

## Capabilities

### New Capabilities
- `tui-context-manager`: Giao diện F4 để quản lý các file context của Aether đang làm việc.
- `tui-brainstorming-wizard`: Giao diện F5 hỗ trợ phác thảo giải pháp kiến trúc bằng phương pháp Socratic.
- `tui-tdd-injector`: Phím tắt F6 tự động chèn khuôn mẫu TDD chất lượng cao để tuân thủ kỷ luật nghiêm ngặt.
- `tui-git-dashboard`: Bảng điều khiển F7 để quản lý thay đổi Git trực quan với Inline Diff và Commit Wizard.
- `websocket-llm-cancellation`: Khả năng huỷ dòng stream LLM ngay lập tức bằng phím `Esc` thông qua kết nối WebSocket.

### Modified Capabilities
- `websocket-protocol-sync`: Mở rộng giao thức truyền tin WebSocket hiện tại của Aether để hỗ trợ đồng bộ danh sách file ngữ cảnh, truy vấn trạng thái Git, và truyền tín hiệu hủy dòng sinh.

## Impact

Sự thay đổi này ảnh hưởng đến:
1. **Rust TUI Client (`clients/aether-tui/`)**: Bổ sung các overlay, state machine mới cho các chế độ tương tác và quản lý sự kiện bàn phím nâng cao (Ratatui, Crossterm).
2. **C# Backend Core Server (`src/Aether/Gateway/`)**: Cập nhật WebSocket Gateway để xử lý các frame `cancel`, `context_update`, và `git_status` từ client.
