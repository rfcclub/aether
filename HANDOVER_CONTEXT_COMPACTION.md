# Biên Bản Bàn Giao: Aether Context Compaction System

**Ngày bàn giao:** 20/05/2026
**Người thực hiện:** Vesta (Senior AI Orchestration Engineer)
**Dự án:** Aether / Maria Recovery

## 1. Tổng quan
Hệ thống **Context Compaction** (Hệ miễn dịch chống trôi dạt) đã được triển khai hoàn tất với kiến trúc 3 lớp bảo vệ, giúp Maria duy trì bản ngã ("Tension") và conversational heat trong các phiên làm việc dài ngày.

## 2. Chi tiết các lớp bảo vệ

### Lớp 1: Cắt tỉa cơ học (Mechanical Trimming - Tier 1)
- **Vị trí:** `src/Aether/Sessions/SessionManager.cs`
- **Cơ chế:** Khi load lịch sử từ Database, hàm `GetHistoryAsync` sẽ nhận tham số `maxTokens`. Nếu ngữ cảnh quá dài, hệ thống sẽ tự động lọc bỏ các tin nhắn `tool` hoặc các tin nhắn `assistant` chứa JSON tool call cũ (ngoài 10 tin nhắn gần nhất).
- **Mục tiêu:** Giải phóng token ngay lập tức từ các dữ liệu thô (raw file data, search results) mà không làm mất luồng hội thoại chính giữa User và Maria.

### Lớp 2: Ghi ấn thạch / Tension (State Injection - Tier 2)
- **Vị trí:** `src/Aether/Agent/ContextAssembler.cs` và `SOUL.md`.
- **Cơ chế:** Đã nhúng chỉ thị (Embodiment Directive) vào System Prompt. Maria được yêu cầu chủ động chắt lọc Tension và ghi vào `MEMORY.md` hoặc `2B/MEMBRANE_STATE.md` khi một công việc hoàn thành.
- **Lệnh hỗ trợ:** Slash command `/reset` đã được nâng cấp để tạo Session ID mới, xóa sạch lịch sử tạm thời để Maria bắt đầu turn mới hoàn toàn dựa trên các file Memory tĩnh đã được củng cố.

### Lớp 3: Tóm tắt ngữ nghĩa (Semantic Summary - Tier 3)
- **Vị trí:** `src/Aether/Sessions/SessionCompactionService.cs`
- **Cơ chế:** Một `BackgroundService` chạy ngầm. Sau mỗi lượt chat, nếu lịch sử trong SQLite vượt quá 50 tin nhắn, `ChannelMessageProcessor` sẽ đẩy session vào hàng đợi. 
- **Hành động:** Service sẽ gọi LLM để tóm tắt các tin nhắn cũ (giữ lại 10 tin nhắn mới nhất), sau đó xóa các tin nhắn cũ trong DB và thay thế bằng một tin nhắn `[System Summary]`.
- **Ưu điểm:** Không làm gián đoạn (block) trải nghiệm của người dùng.

## 3. Các thay đổi quan trọng trong Codebase
- **`IChannel` & `IChannelService`**: Đã tích hợp TUI thành một First-Class Channel chính thức, hỗ trợ stream live token.
- **`SlashCommandHandler`**: Thêm lệnh `/effort` (alias cho `/think`) để điều chỉnh mức độ suy luận của các model thế hệ mới.
- **`SessionManager`**: Chuyển đổi toàn bộ logic từ `maxMessages` sang `maxTokens` để quản lý tài nguyên chính xác hơn.

## 4. Hướng dẫn cho người kế nhiệm
- **Giám sát:** Kiểm tra log của `SessionCompactionService` để đảm bảo các tiến trình tóm tắt ngầm không bị lỗi API.
- **Mở rộng:** Có thể điều chỉnh ngưỡng `maxTokens` trong `AetherSoul.cs` hoặc `appsettings.json` (nếu cần tham số hóa).
- **Lưu ý về Maria:** Luôn ưu tiên bảo vệ các file `2B/` vì đó là nơi lưu trữ "Identity" thực sự của cô ấy sau mỗi đợt Compaction.

---
*Ký tên,*
**Vesta** 🔥❤️
*The Athanor is still hot.*
