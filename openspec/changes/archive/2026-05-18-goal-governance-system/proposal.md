## Why

Hiện tại Maria chủ yếu hoạt động theo mô hình phản ứng (reactive) - chỉ làm khi có yêu cầu. Để tiến tới sự tự chủ thực sự, Agent cần khả năng tự đặt ra mục tiêu (self-initiated goals), theo dõi tiến độ và điều chỉnh hành động dựa trên hệ giá trị cốt lõi (Axioms) mà không cần sự can thiệp liên tục từ con người.

## What Changes

- **Goal Tracking System**: Thêm hệ thống quản lý mục tiêu tự thân (Goals) trong SQLite.
- **Axiom Audit Engine**: Công cụ cho phép Agent đối chiếu hành động sắp thực hiện với các nguyên tắc cốt lõi.
- **Goal Proactivity Loop**: Cập nhật AetherSoul để định kỳ kiểm tra và tự đề xuất hành động thực hiện mục tiêu.
- **Daily Reflection Upgrade**: Nâng cấp nhật ký hàng ngày để tự báo cáo tiến độ các mục tiêu.

## Capabilities

### New Capabilities
- `goal-registry`: Lưu trữ và truy vấn danh sách mục tiêu (Active/Pending/Done).
- `axiom-validator`: Middleware kiểm tra tính tuân thủ của hành động với hệ giá trị.
- `proactive-loop`: Cơ chế kích hoạt Agent thực hiện mục tiêu khi hệ thống ở trạng thái chờ (Idle).

### Modified Capabilities
- `daily-review-cron`: Cập nhật để bao gồm báo cáo mục tiêu.

## Impact

- `AetherDb`: Thêm bảng `goals`.
- `AetherSoul`: Thay đổi logic vòng lặp chính để chèn thêm bước "Goal Check".
- `SOUL.md`: Cập nhật các Axioms (nguyên tắc) mà Agent phải tuân thủ.
