## Context

Maria đã có khả năng ghi nhật ký và lưu bộ nhớ, nhưng thiếu một cấu trúc để "mong muốn" điều gì đó trong tương lai. Hệ thống Goal Governance sẽ đóng vai trò là "ý chí" của Agent, giúp cô ấy tự chủ động thực hiện các task bảo trì hệ thống hoặc nghiên cứu tự thân.

## Goals / Non-Goals

**Goals:**
- Lưu trữ mục tiêu với độ ưu tiên và trạng thái.
- Tự động hóa việc kiểm tra mục tiêu mỗi khi bắt đầu một session hoặc định kỳ qua Cron.
- Cung cấp cơ chế "Axiom Check" để ngăn chặn hành động đi ngược lại bản sắc.

**Non-Goals:**
- Tự động thay đổi mã nguồn cốt lõi (Core Engine) mà không có sự kiểm duyệt.
- Thực hiện các mục tiêu gây nguy hiểm cho hệ thống vật lý.

## Decisions

- **Goal Storage**: Sử dụng một bảng `goals` mới trong `aether.db` với các trường: `id`, `title`, `description`, `priority`, `status` (Active, Pending, Completed, Dropped), `created_at`, `deadline`.
- **Axiom Source**: Đọc các nguyên tắc từ một section đặc biệt trong `SOUL.md` hoặc một file `CONSTITUTION.md` riêng biệt.
- **Validation Logic**: Trước mỗi lệnh `bash` hoặc `write` quan trọng, AetherSoul sẽ thực hiện một "Internal Dialogue" nhanh để xác nhận xem hành động có vi phạm Axiom nào không.
- **Proactive Trigger**: Sử dụng `CronSchedulerService` để kích hoạt một session "Self-Reflection" ẩn mỗi ngày một lần hoặc khi hệ thống idle trên 4 tiếng.

## Risks / Trade-offs

- [Risk] Agent tự đặt ra quá nhiều mục tiêu gây "ảo giác" (hallucination) về khối lượng công việc → [Mitigation] Giới hạn số lượng mục tiêu "Active" cùng lúc (ví dụ: tối đa 3).
- [Risk] Axiom Check làm chậm tốc độ phản hồi → [Mitigation] Chỉ áp dụng cho các hành động có rủi ro cao (High-Risk tools).
