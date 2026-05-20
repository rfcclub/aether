## ADDED Requirements

### Requirement: Axiom Definition Format
Các nguyên tắc cốt lõi SHALL được định nghĩa trong `SOUL.md` dưới dạng danh sách các Axioms có đánh số.

#### Scenario: Parse axioms
- **WHEN** Agent khởi động
- **THEN** hệ thống nạp toàn bộ danh sách Axioms vào bộ nhớ để sử dụng cho validation

### Requirement: Justification Log
Mọi quyết định chặn hành động do vi phạm Axiom SHALL được ghi lại trong log hệ thống và nhật ký của Agent.

#### Scenario: Log violation
- **WHEN** một Axiom bị vi phạm
- **THEN** một bản ghi chi tiết về lý do và bối cảnh được lưu lại
