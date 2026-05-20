## Why

Aether đang tiến tới mô hình "Colony" - nơi nhiều Agent chuyên biệt cùng làm việc. Để tránh việc các Agent bị cô lập thông tin và lặp lại công việc, cần một cơ chế đồng bộ tri thức (sync) với hệ thống hive Agora và một kiến trúc điều phối (orchestration) cho phép các Agent giao tiếp trực tiếp.

## What Changes

- **Agora Sync Module**: Tự động xuất bản các phát hiện quan trọng (research findings) từ Aether sang thư mục Agora chung.
- **Agent-to-Agent Communication**: Thêm khả năng cho phép một Agent "triệu hồi" Agent khác để hỗ trợ (ví dụ: Maria gọi Vesta để fix build).
- **Shared Memory Layer**: Triển khai một lớp bộ nhớ dùng chung (Global Memory) bên cạnh bộ nhớ riêng của từng Agent.
- **Dynamic Routing**: Cập nhật MessageRouter để điều hướng tin nhắn dựa trên kỹ năng của Agent (Skill-based routing).

## Capabilities

### New Capabilities
- `agora-sync-engine`: Hệ thống theo dõi file thay đổi trong workspace và đồng bộ sang Agora.
- `agent-call-tool`: Công cụ mới cho phép Agent gửi yêu cầu xử lý sang một Agent profile khác.
- `global-memory-system`: Lớp trừu tượng cho bộ nhớ dùng chung giữa các Agent trong Colony.

### Modified Capabilities
- `llm-router`: Cập nhật để hỗ trợ định tuyến tin nhắn theo kỹ năng Agent.

## Impact

- `~/agora/`: Thư mục đồng bộ tri thức.
- `src/Aether/Routing`: Đại tu MessageRouter để hỗ trợ multi-agent.
- `src/Aether/Memory`: Thêm implementation cho GlobalMemory.
