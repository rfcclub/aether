## ADDED Requirements

### Requirement: Colony-wide Identity Awareness
Mỗi Agent SHALL nhận biết được danh sách các thành viên khác trong Colony và kỹ năng của họ.

#### Scenario: List available agents
- **WHEN** Agent truy vấn trạng thái Colony
- **THEN** hệ thống trả về danh sách các Agent đang hoạt động và mô tả kỹ năng của họ

### Requirement: Memory Promotion to Global
Agent SHALL có khả năng "đề xuất" (promote) một mảng ký ức cá nhân lên thành tri thức chung của Colony.

#### Scenario: Promote local memory
- **WHEN** Maria quyết định một thông tin về Thoor là quan trọng với tất cả
- **THEN** thông tin đó được ghi vào Global Memory database
