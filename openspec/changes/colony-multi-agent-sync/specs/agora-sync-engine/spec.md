## ADDED Requirements

### Requirement: Automatic Research Sync
Hệ thống SHALL tự động sao chép các file Markdown trong thư mục `research/` sang thư mục Agora chung khi có thay đổi.

#### Scenario: Sync new research
- **WHEN** Agent tạo một file research mới
- **THEN** file đó xuất hiện trong thư mục `~/agora/` trong vòng 5 giây

### Requirement: Shared Fact Registry
Hệ thống SHALL cung cấp một kho lưu trữ các sự thật (facts) dùng chung mà tất cả agent trong Colony đều có quyền đọc.

#### Scenario: Read shared fact
- **WHEN** Vesta lưu một sự thật về cấu trúc project
- **THEN** Maria có thể truy cập sự thật đó ngay lập tức qua Global Memory
