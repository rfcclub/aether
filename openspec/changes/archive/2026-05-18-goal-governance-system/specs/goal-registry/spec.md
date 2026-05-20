## ADDED Requirements

### Requirement: Goal Persistence
Hệ thống SHALL cho phép lưu trữ, cập nhật và truy vấn các mục tiêu tự thân của Agent trong SQLite.

#### Scenario: Create a new goal
- **WHEN** Agent quyết định tự đặt ra một mục tiêu mới
- **THEN** mục tiêu đó được lưu vào database với trạng thái 'Active'

### Requirement: Axiom Consistency Check
Trước khi thực hiện các hành động có rủi ro cao, hệ thống SHALL đối chiếu hành động đó với các Axioms được định nghĩa trong `SOUL.md`.

#### Scenario: Block action violating axiom
- **WHEN** một hành động vi phạm nguyên tắc "Bảo vệ quyền riêng tư người dùng"
- **THEN** hệ thống chặn hành động và yêu cầu Agent giải trình hoặc hủy bỏ
