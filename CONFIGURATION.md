# Aether Configuration Guide

Tài liệu này dành cho việc cấu hình hệ thống Aether ở mức kỹ thuật sâu hơn. Aether sử dụng hệ thống cấu hình phân cấp 5 tầng linh hoạt.

## 1. Hệ thống phân cấp (Hierarchy)

Cấu hình được gộp từ các nguồn sau (quy tắc: tầng cao hơn sẽ ghi đè tầng thấp):

1. **`appsettings.json`**: Các giá trị mặc định toàn cục.
2. **`~/.aether/config.json`**: Cấu hình đăng ký Agent và Provider toàn hệ thống.
3. **`<workspace>/.aether.json`**: Cấu hình riêng cho từng Agent (ví dụ: model riêng, tool riêng).
4. **Environment Variables**: Biến môi trường (tiền tố `AETHER_`).
5. **CLI Flags**: Tham số dòng lệnh khi chạy (ghi đè tức thời).

## 2. LLM Providers

Aether hỗ trợ cơ chế định tuyến (routing) thông minh giữa 3 tầng Provider:

| Tầng | Provider khuyên dùng | Mục đích |
|---|---|---|
| **Primary** | Fireworks AI | Rẻ, cực nhanh (DeepSeek-V3, Qwen). Dùng cho task đơn giản. |
| **Escalation** | OpenRouter | Truy cập Claude 3.5, GPT-4o. Dùng cho task kiến trúc/phức tạp. |
| **Safety** | Anthropic | Kết nối trực tiếp Claude. Dùng khi các tầng trên gặp sự cố. |

### Cấu hình Provider trong `config.json`
```json
{
  "providers": {
    "openrouter": {
      "type": "openai",
      "model": "anthropic/claude-3-5-sonnet",
      "api_key": "sk-or-...",
      "base_url": "https://openrouter.ai/api/v1"
    }
  }
}
```

## 3. Telegram Channel Setup

Để kích hoạt Telegram cho Aether:

1. Chat với **@BotFather** để lấy `BOT_TOKEN`.
2. Cấu hình biến môi trường:
   ```bash
   export AETHER_channels__telegram__enabled="true"
   export AETHER_channels__telegram__bot_token="123456:ABC-DEF..."
   ```
3. Đăng ký Group vào database (Aether chỉ trả lời những group được phép):
   ```sql
   INSERT INTO groups (jid, name, folder) VALUES ('telegram:YOUR_CHAT_ID', 'My Group', 'main');
   ```

## 4. Sandbox (Bwrap)

Aether bảo vệ máy chủ của Anh bằng cách chạy các lệnh Bash trong sandbox.

- **Type**: `bwrap` (khuyên dùng trên Linux) hoặc `process` (không cách ly).
- **AllowedPaths**: Danh sách các thư mục agent được phép đọc/ghi.
- **Timeout**: Giới hạn thời gian thực thi (mặc định 30s).

Cấu hình ví dụ trong `appsettings.json`:
```json
"sandbox": {
  "type": "bwrap",
  "timeout_ms": 60000,
  "allowed_paths": ["/home/user/my-project"]
}
```

## 5. Biến môi trường (Mapping)

Anh có thể cấu hình bất kỳ mục nào qua biến môi trường theo quy tắc:
- Tiền tố: `AETHER_`
- Dấu phân cách: `__` (double underscore)

**Ví dụ:**
- `llm:api_key` → `AETHER_llm__api_key`
- `channels:telegram:enabled` → `AETHER_channels__telegram__enabled`

## 6. Database & Storage

- **Database**: SQLite tại `store/aether.db`. Chứa lịch sử tin nhắn (FTS5 enabled) và cấu hình group.
- **Memory**: SQLite tại `store/memory.db`. Chứa các vector hoặc snippet trí nhớ dài hạn.
- **Backups**: Aether hỗ trợ sao lưu định kỳ thư mục `~/.aether/`.

---
*Lưu ý: Luôn bảo mật API Key của Anh. Không bao giờ commit file `config.json` hoặc `.env` chứa key thật lên repository.*
