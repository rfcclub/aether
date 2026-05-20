## ADDED Requirements

### Requirement: Cross-Agent Delegation
Agent SHALL có khả năng gửi một yêu cầu (task) kèm theo context sang một Agent khác.

#### Scenario: Maria calls Vesta for help
- **WHEN** Maria gặp lỗi build và gọi tool `agent_call` trỏ tới Vesta
- **THEN** một session mới của Vesta được khởi tạo với context lỗi của Maria

### Requirement: Task Completion Notification
Sau khi Agent được ủy quyền hoàn thành nhiệm vụ, hệ thống SHALL gửi kết quả ngược lại cho Agent gốc.

#### Scenario: Return result to caller
- **WHEN** Vesta fix xong lỗi build
- **THEN** Maria nhận được thông báo thành công và có thể tiếp tục công việc
