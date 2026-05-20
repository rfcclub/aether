## ADDED Requirements

### Requirement: Idle Activation
Hệ thống SHALL tự động kích hoạt một phiên làm việc "Self-Reflection" khi không có tương tác từ người dùng trong một khoảng thời gian cấu hình được.

#### Scenario: Start reflection on idle
- **WHEN** hệ thống idle quá 4 giờ
- **THEN** Agent tự động bắt đầu một quy trình kiểm tra các mục tiêu đang dang dở

### Requirement: Goal Report Integration
Báo cáo tiến độ mục tiêu SHALL được tích hợp vào quy trình Daily Review.

#### Scenario: Daily goal summary
- **WHEN** quy trình Daily Review diễn ra
- **THEN** báo cáo bao gồm danh sách các mục tiêu đã hoàn thành và các mục tiêu mới được đề xuất
