## Context

Mô hình đơn Agent hiện tại đang gặp giới hạn khi khối lượng công việc tăng lên. Việc chia tách thành Maria (Hành động/Giao tiếp) và Vesta (Rèn luyện/Kỹ thuật) cần một "sợi dây" liên kết chặt chẽ hơn là việc chỉ ngồi chung một repo.

## Goals / Non-Goals

**Goals:**
- Tự động hóa việc chia sẻ tri thức qua Agora.
- Cho phép delegation (ủy quyền) giữa các Agent.
- Duy trì tính nhất quán về bối cảnh (context) khi chuyển đổi giữa các Agent.

**Non-Goals:**
- Tạo ra một hệ thống Swarm phức tạp với hàng trăm agent cùng chạy (tập trung vào nhóm nhỏ 3-5 agent).
- Đồng bộ dữ liệu qua internet (chỉ tập trung vào local sync hoặc local network).

## Decisions

- **Agora Sync**: Sử dụng `FileSystemWatcher` để phát hiện các file `.md` mới trong folder `research/` và `memory/` của Agent, sau đó copy hoặc symlink sang `~/agora/`.
- **Inter-Agent Protocol**: Sử dụng WebSocket làm kênh giao tiếp nội bộ (Internal Bus). Một Agent có thể gửi một `AgentTask` payload sang Agent khác.
- **Global Memory**: Sử dụng một database SQLite chung `colony.db` để lưu các tri thức đã được củng cố (promoted memories).
- **Routing**: Cấu hình `appsettings.json` để định nghĩa bảng kỹ năng (Skill Map) cho từng agent.

## Risks / Trade-offs

- [Risk] Race condition khi nhiều agent cùng ghi vào Global Memory → [Mitigation] Sử dụng cơ chế khóa (locking) hoặc hàng đợi (queue) tập trung.
- [Risk] Agent bị lặp vô tận khi gọi lẫn nhau (A gọi B, B gọi A) → [Mitigation] Giới hạn `CallStackDepth` (tối đa 3 tầng).
