## 1. Database & Schema

- [x] 1.1 Tạo migration thêm bảng `goals` vào `aether.db`.
- [x] 1.2 Implement `GoalStore` để thực hiện các thao tác CRUD trên mục tiêu.

## 2. Axiom Engine

- [x] 2.1 Cập nhật `SOUL.md` với section `# Axioms` chuẩn hóa.
- [x] 2.2 Tạo middleware `AxiomValidator` trong AetherSoul.
- [x] 2.3 Tích hợp bước kiểm tra Axiom trước khi thực hiện các tool nhạy cảm (bash, write).

## 3. Proactive Loop

- [x] 3.1 Implement `ProactiveTaskService` kiểm tra hệ thống idle.
- [x] 3.2 Thêm logic kích hoạt Agent tự suy ngẫm về mục tiêu khi idle.

## 4. Reporting

- [x] 4.1 Nâng cấp `DailyReviewHostedService` để truy vấn dữ liệu từ `GoalStore`.
- [x] 4.2 Cập nhật template báo cáo `patches/reflections-*.md` để bao gồm mục tiêu.
